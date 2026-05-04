using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace psu_archive_explorer
{
    public class AdxPreviewPanel : UserControl
    {
        // ---- Audio state ----
        private WaveOutEvent outputDevice;
        private WaveFileReader waveReader;
        private MemoryStream wavStream;

        // ---- UI ----
        private readonly Label lblInfo;
        private readonly Panel playerPanel;
        private readonly Button btnPlayPause;
        private readonly Button btnStop;
        private readonly TrackBar trackBarProgress;
        private readonly Label lblTime;
        private readonly Label lblStatus;
        private readonly System.Windows.Forms.Timer progressTimer = new System.Windows.Forms.Timer();
        private bool isUserSeeking = false;
        private bool audioReady = false;
        private string _displayName;
        private readonly CancellationTokenSource decodeCts = new CancellationTokenSource();

        public AdxPreviewPanel(string filePath, string infoText, string displayName = null)
            : this(infoText, displayName ?? (filePath != null ? Path.GetFileName(filePath) : null))
        {
            BeginLoadAdxAsync(filePath);
        }

        /// <summary>
        /// Byte array overload. Use this when the ADX data is already in memory
        /// (e.g. extracted from an archive container) so we don't write it to
        /// disk just to read it back.
        /// </summary>
        public AdxPreviewPanel(byte[] adxData, string infoText, string displayName = null)
            : this(infoText, displayName)
        {
            BeginDecodeBytesAsync(adxData);
        }

        /// <summary>
        /// Shared UI construction. The two public constructors chain here, then
        /// kick off their own (path based or byte based) decode.
        /// </summary>
        private AdxPreviewPanel(string infoText, string displayName)
        {
            _displayName = displayName;

            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(229, 229, 229);

            // ---- Info label (top) ----
            lblInfo = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10.5f),
                Text = infoText,
                Padding = new Padding(10)
            };

            // ---- Player panel (bottom, fixed height) ----
            playerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 140,
                BackColor = Color.FromArgb(255, 255, 255),
                Padding = new Padding(10)
            };

            btnPlayPause = new Button
            {
                Text = "▶ Play",
                Location = new Point(10, 10),
                Size = new Size(100, 32),
                Font = new Font("Segoe UI", 10F),
                Enabled = false
            };
            btnPlayPause.Click += (s, e) => TogglePlayPause();

            btnStop = new Button
            {
                Text = "⏹ Stop",
                Location = new Point(120, 10),
                Size = new Size(80, 32),
                Font = new Font("Segoe UI", 10F),
                Enabled = false
            };
            btnStop.Click += (s, e) => StopPlayback();

            lblStatus = new Label
            {
                Location = new Point(210, 17),
                AutoSize = true,
                ForeColor = Color.DarkGray,
                Font = new Font("Segoe UI", 9F),
                Text = "Decoding..."
            };

            trackBarProgress = new TrackBar
            {
                Location = new Point(10, 50),
                Minimum = 0,
                Maximum = 1000,
                TickFrequency = 50,
                TickStyle = TickStyle.None, 
                Enabled = false
            };
            trackBarProgress.Scroll += (s, e) => UpdateTimeLabel();
            trackBarProgress.MouseDown += (s, e) => isUserSeeking = true;
            trackBarProgress.MouseUp += (s, e) => { isUserSeeking = false; SeekToPosition(); };

            lblTime = new Label
            {
                Location = new Point(10, 105),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                Text = "0:00 / 0:00"
            };

            playerPanel.Controls.Add(btnPlayPause);
            playerPanel.Controls.Add(btnStop);
            playerPanel.Controls.Add(lblStatus);
            playerPanel.Controls.Add(trackBarProgress);
            playerPanel.Controls.Add(lblTime);

            // Resize trackbar with panel
            playerPanel.Resize += (s, e) =>
            {
                trackBarProgress.Width = playerPanel.ClientSize.Width - 20;
            };

            // add Fill last so Bottom docks first
            this.Controls.Add(lblInfo);
            this.Controls.Add(playerPanel);

            progressTimer.Interval = 200;
            progressTimer.Tick += ProgressTimer_Tick;
        }

        /// <summary>
        /// Reads + decodes the ADX on a background thread, then marshals the
        /// decoded WAV bytes back to the UI thread to wire up NAudio.
        /// </summary>
        private void BeginLoadAdxAsync(string filePath)
        {
            CancellationToken ct = decodeCts.Token;

            Task.Run(() =>
            {
                try
                {
                    byte[] adxData = File.ReadAllBytes(filePath);
                    ct.ThrowIfCancellationRequested();
                    return DecodeBytesToResult(adxData, ct);
                }
                catch (OperationCanceledException)
                {
                    return new DecodeResult { Canceled = true };
                }
                catch (Exception ex)
                {
                    return new DecodeResult { Success = false, ErrorMessage = $"Preview failed: {ex.Message}" };
                }
            }, ct).ContinueWith(t => MarshalDecodeResultToUi(t, ct), TaskScheduler.Default);
        }

        /// <summary>
        /// Decode ADX bytes that are already in memory (e.g. pulled from an
        /// archive container). Same flow as <see cref="BeginLoadAdxAsync"/>
        /// minus the disk read.
        /// </summary>
        private void BeginDecodeBytesAsync(byte[] adxData)
        {
            CancellationToken ct = decodeCts.Token;

            Task.Run(() =>
            {
                try
                {
                    if (adxData == null || adxData.Length == 0)
                        return new DecodeResult { Success = false, ErrorMessage = "Preview unavailable: empty ADX data." };

                    ct.ThrowIfCancellationRequested();
                    return DecodeBytesToResult(adxData, ct);
                }
                catch (OperationCanceledException)
                {
                    return new DecodeResult { Canceled = true };
                }
                catch (Exception ex)
                {
                    return new DecodeResult { Success = false, ErrorMessage = $"Preview failed: {ex.Message}" };
                }
            }, ct).ContinueWith(t => MarshalDecodeResultToUi(t, ct), TaskScheduler.Default);
        }

        /// <summary>
        /// Runs the AdxDecoder on the given bytes and wraps the outcome in a
        /// DecodeResult. Executed on the background thread.
        /// </summary>
        private static DecodeResult DecodeBytesToResult(byte[] adxData, CancellationToken ct)
        {
            try
            {
                byte[] wavBytes = AdxDecoder.DecodeToWav(adxData);
                ct.ThrowIfCancellationRequested();
                return new DecodeResult { Success = true, WavBytes = wavBytes };
            }
            catch (OperationCanceledException)
            {
                return new DecodeResult { Canceled = true };
            }
            catch (NotSupportedException ex)
            {
                return new DecodeResult { Success = false, ErrorMessage = $"Preview unavailable: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new DecodeResult { Success = false, ErrorMessage = $"Preview failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Shared continuation that marshals a completed DecodeResult back onto
        /// the UI thread, guarding against the panel having been disposed while
        /// the decode was in flight.
        /// </summary>
        private void MarshalDecodeResultToUi(Task<DecodeResult> t, CancellationToken ct)
        {
            // Discard if the panel was already disposed / cancelled
            if (ct.IsCancellationRequested) return;
            if (IsDisposed || Disposing) return;

            DecodeResult result = t.Result;
            if (result.Canceled) return;

            try
            {
                if (InvokeRequired)
                    Invoke((Action)(() => OnDecodeComplete(result)));
                else
                    OnDecodeComplete(result);
            }
            catch (ObjectDisposedException) { /* panel gone, ignore */ }
            catch (InvalidOperationException) { /* handle destroyed, ignore */ }
        }

        private void OnDecodeComplete(DecodeResult result)
        {
            if (IsDisposed || Disposing) return;

            if (!result.Success)
            {
                DisablePlayer(result.ErrorMessage);
                return;
            }

            try
            {
                wavStream = new MemoryStream(result.WavBytes);
                waveReader = new WaveFileReader(wavStream);

                outputDevice = new WaveOutEvent();
                outputDevice.Init(waveReader);
                outputDevice.PlaybackStopped += PlaybackStoppedHandler;

                trackBarProgress.Maximum = (int)Math.Min(waveReader.Length, int.MaxValue);

                audioReady = true;
                btnPlayPause.Enabled = true;
                btnStop.Enabled = true;
                trackBarProgress.Enabled = true;
                lblStatus.Text = "Ready to play";
                lblStatus.ForeColor = Color.DarkGray;
                UpdateTimeLabel();
            }
            catch (Exception ex)
            {
                DisablePlayer($"Preview failed: {ex.Message}");
            }
        }

        private void DisablePlayer(string reason)
        {
            btnPlayPause.Enabled = false;
            btnStop.Enabled = false;
            trackBarProgress.Enabled = false;
            lblStatus.Text = reason;
            lblStatus.ForeColor = Color.Firebrick;
        }

        private void TogglePlayPause()
        {
            if (!audioReady || outputDevice == null || waveReader == null) return;

            try
            {
                string name = _displayName ?? "";
                if (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    outputDevice.Pause();
                    btnPlayPause.Text = "▶ Play";
                    lblStatus.Text = "⏸ Paused " + name;
                    progressTimer.Stop();
                }
                else
                {
                    if (waveReader.Position >= waveReader.Length)
                        waveReader.Position = 0;

                    outputDevice.Play();
                    btnPlayPause.Text = "⏸ Pause";
                    lblStatus.Text = "▶ Playing " + name;
                    progressTimer.Start();
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Playback error: {ex.Message}";
            }
        }

        private void StopPlayback()
        {
            if (outputDevice == null || waveReader == null) return;

            try
            {
                outputDevice.Stop();
                waveReader.Position = 0;
                trackBarProgress.Value = 0;
                btnPlayPause.Text = "▶ Play";
                lblStatus.Text = "Stopped " + (_displayName ?? "");
                UpdateTimeLabel();
                progressTimer.Stop();
            }
            catch { }
        }

        private void PlaybackStoppedHandler(object sender, StoppedEventArgs args)
        {
            if (IsDisposed || Disposing) return;

            if (InvokeRequired)
            {
                try { Invoke((Action)OnPlaybackFinished); } catch { }
            }
            else
            {
                OnPlaybackFinished();
            }
        }

        private void OnPlaybackFinished()
        {
            progressTimer.Stop();
            btnPlayPause.Text = "▶ Play";
            if (waveReader != null && waveReader.Position >= waveReader.Length)
            {
                lblStatus.Text = "Playback finished";
                trackBarProgress.Value = 0;
                waveReader.Position = 0;
                UpdateTimeLabel();
            }
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            if (isUserSeeking || waveReader == null) return;

            try
            {
                long pos = waveReader.Position;
                if (pos < 0) pos = 0;
                if (pos > trackBarProgress.Maximum) pos = trackBarProgress.Maximum;
                trackBarProgress.Value = (int)pos;
                UpdateTimeLabel();
            }
            catch { }
        }

        private void SeekToPosition()
        {
            if (waveReader == null) return;

            try
            {
                long target = trackBarProgress.Value;
                if (target > waveReader.Length) target = waveReader.Length;
                waveReader.Position = target;
                UpdateTimeLabel();
            }
            catch { }
        }

        private void UpdateTimeLabel()
        {
            if (waveReader == null)
            {
                lblTime.Text = "0:00 / 0:00";
                return;
            }

            TimeSpan cur = waveReader.CurrentTime;
            TimeSpan tot = waveReader.TotalTime;
            lblTime.Text = $"{(int)cur.TotalMinutes:D1}:{cur.Seconds:D2} / " +
                           $"{(int)tot.TotalMinutes:D1}:{tot.Seconds:D2}";
        }

        /// <summary>
        /// Stops playback and releases all audio resources. Called automatically
        /// on dispose, which fires when the parent panel's controls are cleared.
        /// </summary>
        private void CleanupAudio()
        {
            try { progressTimer.Stop(); } catch { }

            try
            {
                if (outputDevice != null)
                {
                    outputDevice.PlaybackStopped -= PlaybackStoppedHandler;
                    outputDevice.Stop();
                    outputDevice.Dispose();
                }
            }
            catch { }
            outputDevice = null;

            try { if (waveReader != null) waveReader.Dispose(); } catch { }
            waveReader = null;

            try { if (wavStream != null) wavStream.Dispose(); } catch { }
            wavStream = null;

            audioReady = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Cancel any in flight decode so its completion handler no ops
                try { decodeCts.Cancel(); } catch { }
                try { decodeCts.Dispose(); } catch { }

                CleanupAudio();
                progressTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // Small DTO for shuttling background thread results back to UI thread
        private class DecodeResult
        {
            public bool Success;
            public bool Canceled;
            public byte[] WavBytes;
            public string ErrorMessage;
        }
    }
}