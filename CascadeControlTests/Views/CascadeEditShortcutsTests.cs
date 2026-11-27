using CascadeControl.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Views
{
    /// <summary>
    /// <see cref="CascadeEditShortcuts"/> 的對照表：逐一列出檢視控制項的全部編輯快捷鍵。
    /// </summary>
    /// <remarks>
    /// <see cref="CascadeEditCommand"/> 是 internal，不能出現在 public 測試方法的參數，所以用 nameof 傳指令名稱。
    /// </remarks>
    [TestClass]
    public class CascadeEditShortcutsTests
    {
        [DataTestMethod]
        [DataRow(Keys.Escape, nameof(CascadeEditCommand.ClearSelection))]
        [DataRow(Keys.Control | Keys.C, nameof(CascadeEditCommand.Copy))]
        [DataRow(Keys.Control | Keys.Shift | Keys.C, nameof(CascadeEditCommand.CopyPath))]
        [DataRow(Keys.Control | Keys.A, nameof(CascadeEditCommand.SelectAll))]
        [DataRow(Keys.Control | Keys.Space, nameof(CascadeEditCommand.ToggleCheck))]
        [DataRow(Keys.Control | Keys.Z, nameof(CascadeEditCommand.Undo))]
        [DataRow(Keys.Control | Keys.Y, nameof(CascadeEditCommand.Redo))]
        [DataRow(Keys.Control | Keys.Shift | Keys.Z, nameof(CascadeEditCommand.Redo))]
        [DataRow(Keys.Control | Keys.X, nameof(CascadeEditCommand.Cut))]
        [DataRow(Keys.Control | Keys.V, nameof(CascadeEditCommand.Paste))]
        [DataRow(Keys.Control | Keys.Shift | Keys.V, nameof(CascadeEditCommand.PasteBefore))]
        [DataRow(Keys.Control | Keys.Alt | Keys.V, nameof(CascadeEditCommand.PasteAsChild))]
        [DataRow(Keys.Delete, nameof(CascadeEditCommand.Delete))]
        [DataRow(Keys.F2, nameof(CascadeEditCommand.Rename))]
        [DataRow(Keys.Control | Keys.I, nameof(CascadeEditCommand.Insert))]
        [DataRow(Keys.Control | Keys.Shift | Keys.I, nameof(CascadeEditCommand.InsertBefore))]
        [DataRow(Keys.Control | Keys.N, nameof(CascadeEditCommand.CreateChild))]
        public void TryGetCommand_BoundKey_ReturnsCommand(Keys keys, string command)
        {
            Assert.IsTrue(CascadeEditShortcuts.TryGetCommand(keys, out var actual));
            Assert.AreEqual(command, actual.ToString());
        }

        [DataTestMethod]
        [DataRow(Keys.C)]
        [DataRow(Keys.Enter)]
        [DataRow(Keys.Space)]
        [DataRow(Keys.Shift | Keys.Space)]
        [DataRow(Keys.Shift | Keys.Delete)]
        [DataRow(Keys.Control | Keys.Escape)]
        [DataRow(Keys.Control | Keys.Alt | Keys.C)]
        [DataRow(Keys.Control | Keys.Shift | Keys.N)]
        [DataRow(Keys.Control | Keys.Shift | Keys.Y)]
        public void TryGetCommand_UnboundKey_ReturnsFalse(Keys keys)
        {
            Assert.IsFalse(CascadeEditShortcuts.TryGetCommand(keys, out _));
        }

        [DataTestMethod]
        [DataRow(nameof(CascadeEditCommand.ClearSelection), "Esc", false)]
        [DataRow(nameof(CascadeEditCommand.Copy), "Ctrl+C", false)]
        [DataRow(nameof(CascadeEditCommand.CopyPath), "Ctrl+Shift+C", false)]
        [DataRow(nameof(CascadeEditCommand.SelectAll), "Ctrl+A", false)]
        [DataRow(nameof(CascadeEditCommand.ToggleCheck), "Ctrl+Space", false)]
        [DataRow(nameof(CascadeEditCommand.Undo), "Ctrl+Z", false)]
        [DataRow(nameof(CascadeEditCommand.Redo), "Ctrl+Y", false)]
        [DataRow(nameof(CascadeEditCommand.Cut), "Ctrl+X", true)]
        [DataRow(nameof(CascadeEditCommand.Paste), "Ctrl+V", true)]
        [DataRow(nameof(CascadeEditCommand.PasteBefore), "Ctrl+Shift+V", true)]
        [DataRow(nameof(CascadeEditCommand.PasteAsChild), "Ctrl+Alt+V", true)]
        [DataRow(nameof(CascadeEditCommand.Delete), "Del", true)]
        [DataRow(nameof(CascadeEditCommand.Rename), "F2", true)]
        [DataRow(nameof(CascadeEditCommand.Insert), "Ctrl+I", true)]
        [DataRow(nameof(CascadeEditCommand.InsertBefore), "Ctrl+Shift+I", true)]
        [DataRow(nameof(CascadeEditCommand.CreateChild), "Ctrl+N", true)]
        public void Command_DisplayStringAndChangesTree(string command, string display, bool changesTree)
        {
            var parsed = (CascadeEditCommand)Enum.Parse(typeof(CascadeEditCommand), command);

            Assert.AreEqual(display, CascadeEditShortcuts.GetDisplayString(parsed));
            Assert.AreEqual(changesTree, CascadeEditShortcuts.ChangesTree(parsed));
        }

        [TestMethod]
        public void EveryCommand_HasAtLeastOneKey()
        {
            foreach (var command in Enum.GetValues(typeof(CascadeEditCommand)).Cast<CascadeEditCommand>())
            {
                Assert.IsFalse(string.IsNullOrEmpty(CascadeEditShortcuts.GetDisplayString(command)), command.ToString());
            }
        }
    }
}