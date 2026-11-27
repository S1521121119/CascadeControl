using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    /// <summary>
    /// 檢視控制項的編輯快捷鍵：<see cref="Keys"/> 對應到 <see cref="CascadeEditCommand"/>。
    /// </summary>
    /// <remarks>
    /// 檢視控制項在 ProcessCmdKey 查這張表，右鍵功能表顯示的快捷鍵也從這張表讀，兩邊的綁定才不會各自走樣。
    /// 導覽鍵（方向鍵、Enter、Space、字元跳轉）依呈現方式而不同，不在這張表裡。
    /// </remarks>
    internal static class CascadeEditShortcuts
    {
        // 同一個指令有多個按鍵時，排在前面的是右鍵功能表顯示的那一個。
        private static readonly KeyValuePair<Keys, CascadeEditCommand>[] Bindings =
        {
            Bind(Keys.Escape, CascadeEditCommand.ClearSelection),
            Bind(Keys.Control | Keys.C, CascadeEditCommand.Copy),
            Bind(Keys.Control | Keys.Shift | Keys.C, CascadeEditCommand.CopyPath),
            Bind(Keys.Control | Keys.A, CascadeEditCommand.SelectAll),
            Bind(Keys.Control | Keys.Space, CascadeEditCommand.ToggleCheck),
            Bind(Keys.Control | Keys.Z, CascadeEditCommand.Undo),
            Bind(Keys.Control | Keys.Y, CascadeEditCommand.Redo),
            Bind(Keys.Control | Keys.Shift | Keys.Z, CascadeEditCommand.Redo),
            Bind(Keys.Control | Keys.X, CascadeEditCommand.Cut),
            Bind(Keys.Control | Keys.V, CascadeEditCommand.Paste),
            Bind(Keys.Control | Keys.Shift | Keys.V, CascadeEditCommand.PasteBefore),
            Bind(Keys.Control | Keys.Alt | Keys.V, CascadeEditCommand.PasteAsChild),
            Bind(Keys.Delete, CascadeEditCommand.Delete),
            Bind(Keys.F2, CascadeEditCommand.Rename),
            Bind(Keys.Control | Keys.I, CascadeEditCommand.Insert),
            Bind(Keys.Control | Keys.Shift | Keys.I, CascadeEditCommand.InsertBefore),
            Bind(Keys.Control | Keys.N, CascadeEditCommand.CreateChild),
        };

        private static readonly Dictionary<Keys, CascadeEditCommand> CommandByKeys = Bindings.ToDictionary(b => b.Key, b => b.Value);

        /// <summary>
        /// 查 <paramref name="keyData"/>（含修飾鍵）對應的指令；沒有對應時回傳 false。
        /// </summary>
        internal static bool TryGetCommand(Keys keyData, out CascadeEditCommand command) => CommandByKeys.TryGetValue(keyData, out command);

        /// <summary>
        /// <paramref name="command"/> 會不會改到樹；會的指令只在控制項可以編輯時才有作用。
        /// </summary>
        internal static bool ChangesTree(CascadeEditCommand command)
        {
            switch (command)
            {
                case CascadeEditCommand.Cut:
                case CascadeEditCommand.Paste:
                case CascadeEditCommand.PasteBefore:
                case CascadeEditCommand.PasteAsChild:
                case CascadeEditCommand.Delete:
                case CascadeEditCommand.Rename:
                case CascadeEditCommand.Insert:
                case CascadeEditCommand.InsertBefore:
                case CascadeEditCommand.CreateChild:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 右鍵功能表顯示的快捷鍵文字，例如 "Ctrl+Shift+I"、"Del"。
        /// </summary>
        internal static string GetDisplayString(CascadeEditCommand command) => Format(Bindings.First(b => b.Value == command).Key);

        private static string Format(Keys keys)
        {
            var parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");

            var key = keys & Keys.KeyCode;
            parts.Add(key == Keys.Delete ? "Del" : key == Keys.Escape ? "Esc" : key.ToString());
            return string.Join("+", parts);
        }

        private static KeyValuePair<Keys, CascadeEditCommand> Bind(Keys keys, CascadeEditCommand command) => new KeyValuePair<Keys, CascadeEditCommand>(keys, command);
    }
}