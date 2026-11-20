using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControl.Views.Columns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Columns
{
    /// <summary>
    /// 給衍生類別用的掛勾：<see cref="CascadeView.NodeMouseClick"/>、DrawDefaultNode(e, textBounds)、
    /// <see cref="CascadeView.InvalidateNodes"/>、IsColumnFocused、可覆寫的 <see cref="CascadeView.SetTree"/>。
    /// </summary>
    [TestClass]
    public class CascadeViewHookTests
    {
        private sealed class ProbeView : CascadeView
        {
            public int SetTreeCalls { get; private set; }

            public bool ColumnFocused => IsColumnFocused;

            public override void SetTree(MenuTree tree)
            {
                SetTreeCalls++;
                base.SetTree(tree);
            }

            public void DrawWithTextBounds(CascadeDrawNodeEventArgs e, Rectangle textBounds) => DrawDefaultNode(e, textBounds);
        }

        #region 滑鼠

        [TestMethod]
        public void RightClick_RaisesNodeMouseClickWithoutSelecting()
        {
            using (var host = new ViewHost<CascadeView>())
            {
                var tree = TwoLevelTree();
                host.View.SetTree(tree);

                var events = new List<CascadeNodeMouseEventArgs>();
                ICascadeNode selectedWhenRaised = null;
                host.View.NodeMouseClick += (sender, e) =>
                {
                    events.Add(e);
                    selectedWhenRaised = host.View.SelectedNode;
                };

                var column = host.Columns[0];
                var row = column.GetItemRectangle(1);
                var location = new Point(row.Left + 5, row.Top + 2);
                ViewHost.RaiseMouseUp(column, MouseButtons.Right, location);

                Assert.AreEqual(1, events.Count);
                Assert.AreSame(tree.TopLevelNodes[1], events[0].Node);
                Assert.AreEqual(0, events[0].ColumnIndex);
                Assert.AreEqual(MouseButtons.Right, events[0].Button);
                Assert.AreEqual(location, events[0].Location);
                Assert.AreEqual(row, events[0].Bounds);
                Assert.AreEqual(column.PointToScreen(location), events[0].ScreenLocation);
                Assert.IsNull(selectedWhenRaised, "右鍵不改選取");
                Assert.IsNull(host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void LeftClick_OnSelectionFreeArea_DoesNotSelect_ButRaisesWhenPressAndReleaseMatch()
        {
            using (var host = new ViewHost<SelectionFreeProbeView>())
            {
                var tree = TwoLevelTree();
                host.View.SetTree(tree);

                var events = new List<CascadeNodeMouseEventArgs>();
                host.View.NodeMouseClick += (sender, e) => events.Add(e);

                var column = host.Columns[0];
                var row0 = column.GetItemRectangle(0);
                var row1 = column.GetItemRectangle(1);
                var free0 = new Point(row0.Left + 2, row0.Top + 2);
                var free1 = new Point(row1.Left + 2, row1.Top + 2);

                ViewHost.SendLeftClick(column, free0);
                Assert.IsNull(host.View.SelectedNode, "保留的範圍不改選取");
                Assert.AreEqual(1, events.Count);
                Assert.AreSame(tree.TopLevelNodes[0], events[0].Node);

                ViewHost.SendLeftDrag(column, free0, free1);
                Assert.AreEqual(1, events.Count, "按下與放開不在同一列就不算");
                Assert.IsNull(host.View.SelectedNode);

                ViewHost.SendLeftClick(column, new Point(row1.Left + 40, row1.Top + 2));
                Assert.AreSame(tree.TopLevelNodes[1], host.View.SelectedNode, "其他位置照常選取");
                Assert.AreEqual(2, events.Count);
            }
        }

        [TestMethod]
        public void ClickOnBlankArea_CallsBlankHookWithColumnNodes()
        {
            using (var host = new ViewHost<SelectionFreeProbeView>())
            {
                var tree = TwoLevelTree();
                host.View.SetTree(tree);

                var column = host.Columns[0];
                ViewHost.RaiseMouseUp(column, MouseButtons.Right, new Point(5, column.ClientSize.Height - 2));

                Assert.AreEqual(1, host.View.BlankClicks.Count);
                Assert.AreEqual(0, host.View.BlankClicks[0].Item1);
                CollectionAssert.AreEqual(tree.TopLevelNodes.ToArray(), host.View.BlankClicks[0].Item2.ToArray());
                Assert.IsNull(host.View.SelectedNode);
            }
        }

        // 每一列左邊 10 像素是保留給衍生類別的範圍，並記下空白處的點擊。
        private sealed class SelectionFreeProbeView : CascadeView
        {
            public List<System.Tuple<int, IReadOnlyList<ICascadeNode>>> BlankClicks { get; } = new List<System.Tuple<int, IReadOnlyList<ICascadeNode>>>();

            protected override bool SelectsOnLeftClick(ICascadeNode node, Rectangle bounds, Point location) => location.X >= bounds.Left + 10;

            protected override void OnColumnBlankMouseClick(int columnIndex, IReadOnlyList<ICascadeNode> nodes, MouseButtons button, Point screenLocation)
            {
                BlankClicks.Add(System.Tuple.Create(columnIndex, nodes));
            }
        }

        [TestMethod]
        public void LeftClick_RaisesNodeMouseClick()
        {
            using (var host = new ViewHost<CascadeView>())
            {
                var tree = TwoLevelTree();
                host.View.SetTree(tree);

                var events = new List<CascadeNodeMouseEventArgs>();
                host.View.NodeMouseClick += (sender, e) => events.Add(e);

                var column = host.Columns[0];
                var row = column.GetItemRectangle(0);
                ViewHost.RaiseMouseUp(column, MouseButtons.Left, new Point(row.Left + 5, row.Top + 2));

                Assert.AreEqual(1, events.Count);
                Assert.AreEqual(MouseButtons.Left, events[0].Button);
                Assert.AreSame(tree.TopLevelNodes[0], host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void ClickOnBlankArea_DoesNotRaiseOrSelect()
        {
            using (var host = new ViewHost<CascadeView>())
            {
                host.View.SetTree(TwoLevelTree());

                var raised = 0;
                host.View.NodeMouseClick += (sender, e) => raised++;

                var column = host.Columns[0];
                ViewHost.RaiseMouseUp(column, MouseButtons.Right, new Point(5, column.ClientSize.Height - 2));
                ViewHost.RaiseMouseUp(column, MouseButtons.Middle, new Point(5, 2));

                Assert.AreEqual(0, raised, "空白處與中鍵都不觸發");
                Assert.IsNull(host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void LeftClick_OnBlankAreaOfEarlierColumn_KeepsDeeperSelection()
        {
            using (var host = new ViewHost<SelectionFreeProbeView>())
            {
                var tree = TwoLevelTree();
                host.View.SetTree(tree);

                var a1 = tree.TopLevelNodes[0].Children[0];
                Assert.IsTrue(host.View.TrySelectNode(a1));

                var pathEvents = 0;
                var activated = 0;
                host.View.SelectedPathChanged += (sender, e) => pathEvents++;
                host.View.NodeActivated += (sender, e) => activated++;

                // 原生 ListBox 按在最後一列下方會選到最後一列（B），選取就會跳回第一欄。
                var first = host.Columns[0];
                var blank = new Point(5, first.ClientSize.Height - 2);
                Assert.IsTrue(first.IndexFromPoint(blank) < 0, "測試前提：這個位置是空白處");

                ViewHost.SendLeftClick(first, blank);

                Assert.AreSame(a1, host.View.SelectedNode, "點空白處不改選取");
                Assert.AreEqual(0, pathEvents);
                Assert.AreEqual(1, host.View.BlankClicks.Count);
                Assert.AreEqual(0, host.View.BlankClicks[0].Item1);

                // 操作欄的空白處雙擊也不算選了節點，不觸發 NodeActivated。
                var active = host.Columns[1];
                ViewHost.SendLeftDoubleClick(active, new Point(20, active.ClientSize.Height - 2));

                Assert.AreSame(a1, host.View.SelectedNode);
                Assert.AreEqual(0, pathEvents);
                Assert.AreEqual(0, activated, "空白處雙擊不觸發");
            }
        }

        #endregion 滑鼠

        #region 繪製與其他

        [TestMethod]
        public void DrawDefaultNode_WithTextBounds_FillsWholeRowAndKeepsTextInside()
        {
            using (var view = new ProbeView())
            using (var bitmap = new Bitmap(120, 20))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Magenta);

                var bounds = new Rectangle(0, 0, 120, 20);
                var args = new CascadeDrawNodeEventArgs(
                    g, view.Font, bounds, new CascadeNode("Node", "WWWW"), -1,
                    isSelected: false, isFocusedColumn: false, isEnabled: true);

                view.DrawWithTextBounds(args, new Rectangle(60, 0, 60, 20));
                g.Flush();

                var back = SystemColors.Window.ToArgb();
                Assert.AreEqual(back, bitmap.GetPixel(1, 10).ToArgb(), "背景填滿整列");
                Assert.AreEqual(back, bitmap.GetPixel(119, 19).ToArgb());

                var leftHalfIsBlank = Enumerable.Range(0, 60).All(x => Enumerable.Range(0, 20).All(y => bitmap.GetPixel(x, y).ToArgb() == back));
                var rightHalfHasText = Enumerable.Range(60, 60).Any(x => Enumerable.Range(0, 20).Any(y => bitmap.GetPixel(x, y).ToArgb() != back));
                Assert.IsTrue(leftHalfIsBlank, "文字不可以畫到 textBounds 左邊");
                Assert.IsTrue(rightHalfHasText, "文字要畫在 textBounds 裡");
            }
        }

        [TestMethod]
        public void InvalidateNodes_KeepsColumnsAndSelection()
        {
            using (var view = new CascadeView())
            {
                var tree = TwoLevelTree();
                view.SetTree(tree);
                Assert.IsTrue(view.TrySelectNode(tree.TopLevelNodes[0]));

                var columns = view.Controls.OfType<ListBox>().ToArray();
                var changed = 0;
                view.SelectedPathChanged += (sender, e) => changed++;

                view.InvalidateNodes();

                CollectionAssert.AreEqual(columns, view.Controls.OfType<ListBox>().ToArray(), "不重建欄位");
                Assert.AreSame(tree.TopLevelNodes[0], view.SelectedNode);
                Assert.AreEqual(0, changed);
            }
        }

        [TestMethod]
        public void SetTree_IsVirtual_OverrideRunsThroughBaseReference()
        {
            using (var view = new ProbeView())
            {
                CascadeView asBase = view;
                asBase.SetTree(TwoLevelTree());

                Assert.AreEqual(1, view.SetTreeCalls);
                Assert.AreEqual(1, view.Controls.OfType<ListBox>().Count());
            }
        }

        [TestMethod]
        public void IsColumnFocused_FollowsColumnFocus()
        {
            using (var host = new ViewHost<ProbeView>())
            {
                host.View.SetTree(TwoLevelTree());
                Assert.IsFalse(host.View.ColumnFocused, "還沒把焦點放進來");

                host.Form.Activate();
                host.View.FocusActiveColumn();
                Application.DoEvents();

                if (!host.Columns[0].Focused) Assert.Inconclusive("測試環境拿不到鍵盤焦點（例如沒有互動式桌面）。");
                Assert.IsTrue(host.View.ColumnFocused);
            }
        }

        [TestMethod]
        public void Focus_OnView_HandsFocusToActiveColumn()
        {
            using (var host = new ViewHost<CascadeView>())
            {
                var tree = TwoLevelTree();
                host.View.SetTree(tree);
                Assert.IsTrue(host.View.TrySelectNode(tree.TopLevelNodes[0]));

                host.Form.Activate();
                host.View.Focus();
                Application.DoEvents();

                if (!host.View.ContainsFocus) Assert.Inconclusive("測試環境拿不到鍵盤焦點（例如沒有互動式桌面）。");
                Assert.IsTrue(host.Columns[0].Focused, "焦點轉交給操作欄");
                Assert.IsFalse(host.View.Focused, "控制項本身不留焦點");
            }
        }

        #endregion 繪製與其他

        private static MenuTree TwoLevelTree()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("A|B", "A1|A2");
            return tree;
        }
    }

    /// <summary>
    /// 把控制項放進一個真的顯示出來（但在螢幕外）的表單，讓欄位有 Handle，可以做點擊位置的換算。
    /// </summary>
    internal sealed class ViewHost<TView> : System.IDisposable where TView : CascadeView, new()
    {
        public ViewHost()
        {
            Form = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000),
                ClientSize = new Size(600, 300),
                ShowInTaskbar = false,
            };

            View = new TView { Dock = DockStyle.Fill, ColumnWidth = 120 };
            Form.Controls.Add(View);
            Form.Show();
        }

        public Form Form { get; }

        public TView View { get; }

        public ListBox[] Columns => View.Controls.OfType<ListBox>().OrderBy(c => c.TabIndex).ToArray();

        public void Dispose() => Form.Dispose();
    }

    internal static class ViewHost
    {
        private static readonly MethodInfo OnMouseUp =
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo ProcessCmdKey =
            typeof(Control).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 讓欄位觸發 MouseUp，等同使用者在 <paramref name="location"/>（欄位內座標）放開滑鼠鍵。
        /// </summary>
        public static void RaiseMouseUp(ListBox column, MouseButtons button, Point location)
        {
            OnMouseUp.Invoke(column, new object[] { new MouseEventArgs(button, 1, location.X, location.Y, 0) });
        }

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int MK_LBUTTON = 0x0001;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr SendMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);

        /// <summary>
        /// 對欄位送出真的左鍵按下與放開訊息（會經過 ListBox 原生的處理），等同使用者在 <paramref name="location"/> 點一下。
        /// </summary>
        public static void SendLeftClick(ListBox column, Point location)
        {
            SendMouse(column, WM_LBUTTONDOWN, MK_LBUTTON, location);
            SendMouse(column, WM_LBUTTONUP, 0, location);
        }

        private const int MK_SHIFT = 0x0004;
        private const int MK_CONTROL = 0x0008;

        /// <summary>
        /// 按住 Ctrl（<paramref name="shift"/> 為 true 時再加 Shift）點一下，按鍵狀態放在滑鼠訊息的 wParam 裡。
        /// </summary>
        public static void SendCtrlLeftClick(ListBox column, Point location, bool shift = false) =>
            SendModifiedLeftClick(column, location, control: true, shift: shift);

        /// <summary>
        /// 按住 Shift 點一下。
        /// </summary>
        public static void SendShiftLeftClick(ListBox column, Point location) =>
            SendModifiedLeftClick(column, location, control: false, shift: true);

        private static void SendModifiedLeftClick(ListBox column, Point location, bool control, bool shift)
        {
            var keys = (control ? MK_CONTROL : 0) | (shift ? MK_SHIFT : 0);
            SendMouse(column, WM_LBUTTONDOWN, MK_LBUTTON | keys, location);
            SendMouse(column, WM_LBUTTONUP, keys, location);
        }

        private static readonly MethodInfo OnKeyDown =
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 讓欄位觸發 KeyDown，修飾鍵直接放在 <paramref name="keyData"/> 裡（不看實體鍵盤）。
        /// 只經過控制項的 KeyDown 處理，不經過 ListBox 原生處理；沒被攔下的按鍵（例如一般 ↓）要用 <see cref="SendKey"/>。
        /// </summary>
        /// <returns>按鍵是否被攔下（Handled）。</returns>
        public static bool RaiseKeyDown(ListBox column, Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            OnKeyDown.Invoke(column, new object[] { args });
            return args.Handled;
        }

        private const int WM_KEYDOWN = 0x0100;

        /// <summary>
        /// 對欄位送出真的 WM_KEYDOWN（會經過 ListBox 原生的處理），不帶修飾鍵。
        /// </summary>
        public static void SendKey(ListBox column, Keys key)
        {
            SendMessage(column.Handle, WM_KEYDOWN, (System.IntPtr)(int)key, System.IntPtr.Zero);
        }

        /// <summary>
        /// 在同一個位置快速點兩下：按下、放開、雙擊、放開。
        /// </summary>
        public static void SendLeftDoubleClick(ListBox column, Point location)
        {
            SendLeftClick(column, location);
            SendMouse(column, WM_LBUTTONDBLCLK, MK_LBUTTON, location);
            SendMouse(column, WM_LBUTTONUP, 0, location);
        }

        /// <summary>
        /// 在 <paramref name="from"/> 按下左鍵，拖到 <paramref name="to"/> 才放開。
        /// </summary>
        public static void SendLeftDrag(ListBox column, Point from, Point to)
        {
            SendMouse(column, WM_LBUTTONDOWN, MK_LBUTTON, from);
            SendMouse(column, WM_MOUSEMOVE, MK_LBUTTON, to);
            SendMouse(column, WM_LBUTTONUP, 0, to);
        }

        private static void SendMouse(ListBox column, int message, int keys, Point location)
        {
            var lParam = (location.Y << 16) | (location.X & 0xFFFF);
            SendMessage(column.Handle, message, (System.IntPtr)keys, (System.IntPtr)lParam);
        }

        /// <summary>
        /// 直接呼叫控制項的 ProcessCmdKey，回傳按鍵是否被處理。
        /// </summary>
        public static bool SendCmdKey(Control control, Keys keyData)
        {
            var message = Message.Create(control.Handle, 0x0100, System.IntPtr.Zero, System.IntPtr.Zero);
            return (bool)ProcessCmdKey.Invoke(control, new object[] { message, keyData });
        }
    }
}