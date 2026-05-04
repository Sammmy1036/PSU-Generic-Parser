using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using PSULib.FileClasses.Missions;
using PSULib.FileClasses.Missions.Sets;

namespace psu_archive_explorer.Forms.FileViewers.SetEditorSupportClasses
{
    public partial class HexMetadataEditor : UserControl
    {
        private SetFile.ObjectEntry objectEntry;
        MemoryStream metadataStream;

        public HexMetadataEditor(SetFile.ObjectEntry obj)
        {
            InitializeComponent();
            metadataHexEditor.StringDataVisibility = Visibility.Hidden;
            metadataHexEditor.BytesModified += metadataHexEditor_BytesModified;

            objectEntry = obj;
            metadataStream = new MemoryStream(100);

            // Defer the first data load until the control is actually in the visual tree.
            // Calling RefreshView() before the hex editor is loaded triggers an NRE
            // inside WpfHexaEditor's UpdateHighLight().
            if (IsHandleCreated)
            {
                reloadData();
            }
            else
            {
                this.Load += HexMetadataEditor_FirstLoad;
            }
        }

        private void HexMetadataEditor_FirstLoad(object sender, System.EventArgs e)
        {
            this.Load -= HexMetadataEditor_FirstLoad;
            reloadData();
        }

        public void setObjectEntry(SetFile.ObjectEntry obj)
        {
            objectEntry = obj;
            reloadData();
        }

        private void reloadData()
        {
            if (SetObjectDefinitions.definitions.ContainsKey(objectEntry.objID))
            {
                SetObjectDefinition def = SetObjectDefinitions.definitions[objectEntry.objID];
                metadataLengthLabel.Text = "AotI: " + def.metadataLengthAotI + " / " + "PSP2: " + def.metadataLengthPsp2;
            }
            else
            {
                metadataLengthLabel.Text = "INVALID OBJECT";
            }
            metadataLengthUD.Value = objectEntry.metadata.Length;
            metadataStream.SetLength(objectEntry.metadata.Length);
            metadataStream.Seek(0, SeekOrigin.Begin);
            metadataStream.Write(objectEntry.metadata, 0, objectEntry.metadata.Length);

            // Setting the stream in the constructor marks it unwritable,
            // so we (re)assign it here. Idempotent once set.
            metadataHexEditor.Stream = metadataStream;

            try
            {
                metadataHexEditor.ClearAllChange();
                metadataHexEditor.RefreshView();
            }
            catch (NullReferenceException)
            {
                // Known bug in WpfHexaEditor.UpdateHighLight() when RefreshView
                // runs while the control is mid-rebind. Defer one frame and retry.
                metadataHexEditor.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        metadataHexEditor.ClearAllChange();
                        metadataHexEditor.RefreshView();
                    }
                    catch
                    {
                        // Second failure — swallow so we don't crash the app.
                        // The user can click the .rel file again to retry.
                    }
                }));
            }
        }

        private void metadataHexEditor_BytesModified(object sender, System.EventArgs e)
        {
            objectEntry.metadata = metadataHexEditor.GetAllBytes();
        }

        private void metadataLengthUD_ValueChanged(object sender, System.EventArgs e)
        {
            updateLength();
        }

        private void updateLength()
        {
            lock (metadataHexEditor)
            {
                if (metadataLengthUD.Value != objectEntry.metadata.Length)
                {
                    Array.Resize(ref objectEntry.metadata, Convert.ToInt32(metadataLengthUD.Value));
                    metadataStream.SetLength(objectEntry.metadata.Length);
                    metadataHexEditor.RefreshView();
                }
            }
        }
    }
}
