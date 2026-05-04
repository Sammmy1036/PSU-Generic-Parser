using PSULib.FileClasses.Missions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace psu_archive_explorer.Forms.FileViewers.SetEditorSupportClasses
{
    class SetObjectMetadataEditors
    {
        // Cached for speed, but we have to validate it's still usable each time.
        // MainForm.setRightPanel may dispose whatever control was in the right panel,
        // which disposes this cached editor too.
        private static HexMetadataEditor cachedEditor = null;

        public static UserControl getMetadataEditor(SetFile.ObjectEntry setObject, bool usePortableMode)
        {
            // If the cache is gone or was disposed by the right panel, rebuild.
            if (cachedEditor == null || cachedEditor.IsDisposed)
            {
                cachedEditor = new HexMetadataEditor(setObject);
            }
            else
            {
                cachedEditor.setObjectEntry(setObject);
            }
            return cachedEditor;
        }
    }
}