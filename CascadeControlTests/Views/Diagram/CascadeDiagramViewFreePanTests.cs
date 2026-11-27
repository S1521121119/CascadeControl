using CascadeControl.Trees;
using CascadeControl.Views.Diagram;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Diagram
{
    /// <summary>
    /// <see cref="CascadeDiagramView.AllowFreePan"/> 與右鍵手勢：Shift + 左鍵拖曳與右鍵拖曳自由平移、Esc 還原、按住右鍵 + 滾輪縮放與右鍵選單抑制。
    /// </summary>
    [TestClass]
    public class CascadeDiagramViewFreePanTests
    {
        [TestMethod]
        public void AllowFreePan_DefaultsToTrue()
        {
            using (var view = new CascadeDiagramView())
            {
                Assert.IsTrue(view.AllowFreePan);
            }
        }

        [TestMethod]
        public void ShiftDrag_PansCanvas_WithoutSelectingNode()
        {
            using (var view = CreateView())
            {
                var start = CenterOfFirstNode(view);
                var before = view.Offset();

                view.PanKeyDown = true;
                view.Down(start);
                view.Move(new Point(start.X + 60, start.Y + 25));
                view.Up(new Point(start.X + 60, start.Y + 25));

                var after = view.Offset();
                Assert.AreEqual(before.X + 60, after.X, 1f);
                Assert.AreEqual(before.Y + 25, after.Y, 1f);
                Assert.IsNull(view.SelectedNode, "平移不觸發選取");
            }
        }

        [TestMethod]
        public void ShiftDrag_Escape_RestoresStartingOffset()
        {
            using (var view = CreateView())
            {
                var start = CenterOfFirstNode(view);
                var before = view.Offset();

                view.PanKeyDown = true;
                view.Down(start);
                view.Move(new Point(start.X + 80, start.Y + 30));
                Assert.IsTrue(view.Escape());

                var after = view.Offset();
                Assert.AreEqual(before.X, after.X, 1f);
                Assert.AreEqual(before.Y, after.Y, 1f);
                view.Up(start);
            }
        }

        [TestMethod]
        public void AllowFreePan_False_IgnoresShiftDrag()
        {
            using (var view = CreateView())
            {
                view.AllowFreePan = false;
                var start = CenterOfFirstNode(view);
                var before = view.Offset();

                view.PanKeyDown = true;
                view.Down(start);
                view.Move(new Point(start.X + 60, start.Y + 25));
                view.Up(new Point(start.X + 60, start.Y + 25));

                var after = view.Offset();
                Assert.AreEqual(before.X, after.X, 0.01f);
                Assert.AreEqual(before.Y, after.Y, 0.01f);
            }
        }

        [TestMethod]
        public void AllowFreePan_SetFalse_ClearsExistingPan()
        {
            using (var view = CreateView())
            {
                var before = view.Offset();
                var start = CenterOfFirstNode(view);

                view.PanKeyDown = true;
                view.Down(start);
                view.Move(new Point(start.X + 70, start.Y + 20));
                view.Up(new Point(start.X + 70, start.Y + 20));
                Assert.AreNotEqual(before.X, view.Offset().X);

                view.AllowFreePan = false;
                Assert.AreEqual(before.X, view.Offset().X, 0.01f);
                Assert.AreEqual(before.Y, view.Offset().Y, 0.01f);
            }
        }

        [TestMethod]
        public void RightDrag_PansCanvas_WithoutNodeClickOrContextMenu()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                var start = CenterOfFirstNode(view);
                var end = new Point(start.X + 60, start.Y + 25);
                var before = view.Offset();

                var nodeClicks = 0;
                view.NodeMouseClick += (s, e) => nodeClicks++;

                view.RightDown(start);
                view.RightMove(end);
                Assert.AreEqual(Cursors.SizeAll, view.Cursor);
                view.RightUp(end);

                var after = view.Offset();
                Assert.AreEqual(before.X + 60, after.X, 1f);
                Assert.AreEqual(before.Y + 25, after.Y, 1f);
                Assert.AreEqual(0, nodeClicks, "右鍵拖曳不觸發 NodeMouseClick");
                Assert.IsNull(view.SelectedNode, "右鍵拖曳不觸發選取");

                view.SendContextMenu(view.PointToScreen(end));
                Assert.AreEqual(0, host.ContextMenuOpeningCount, "右鍵拖曳後放開不開啟右鍵功能表");
            }
        }

        [TestMethod]
        public void RightClick_WithoutDrag_RaisesNodeClickAndAllowsContextMenu()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                var start = CenterOfFirstNode(view);
                var slight = new Point(start.X + 1, start.Y + 1);
                var before = view.Offset();

                var nodeClicks = 0;
                view.NodeMouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right) nodeClicks++;
                };

                view.RightDown(start);
                view.RightMove(slight);
                Assert.AreNotEqual(Cursors.SizeAll, view.Cursor);
                view.RightUp(slight);

                var after = view.Offset();
                Assert.AreEqual(before.X, after.X, 0.01f);
                Assert.AreEqual(before.Y, after.Y, 0.01f);
                Assert.AreEqual(1, nodeClicks, "未超過拖曳門檻的右鍵點擊仍觸發 NodeMouseClick");

                view.SendContextMenu(view.PointToScreen(slight));
                Assert.AreEqual(1, host.ContextMenuOpeningCount, "未拖曳的右鍵點擊應開啟右鍵功能表");
            }
        }

        [TestMethod]
        public void RightDrag_Escape_RestoresStartingOffsetAndSuppressesContextMenu()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                var start = CenterOfFirstNode(view);
                var dragged = new Point(start.X + 80, start.Y + 30);
                var before = view.Offset();

                var nodeClicks = 0;
                view.NodeMouseClick += (s, e) => nodeClicks++;

                view.RightDown(start);
                view.RightMove(dragged);
                Assert.IsTrue(view.Escape());

                var after = view.Offset();
                Assert.AreEqual(before.X, after.X, 1f);
                Assert.AreEqual(before.Y, after.Y, 1f);

                view.RightUp(start);
                Assert.AreEqual(0, nodeClicks, "Esc 取消右鍵拖曳後放開不觸發 NodeMouseClick");
                view.SendContextMenu(view.PointToScreen(start));
                Assert.AreEqual(0, host.ContextMenuOpeningCount, "Esc 取消右鍵拖曳後放開不開啟右鍵功能表");
            }
        }

        [TestMethod]
        public void AllowFreePan_False_IgnoresRightDrag_AllowsContextMenu()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                view.AllowFreePan = false;
                var start = CenterOfFirstNode(view);
                var end = new Point(start.X + 60, start.Y + 25);
                var before = view.Offset();

                view.RightDown(start);
                view.RightMove(end);
                view.RightUp(end);

                var after = view.Offset();
                Assert.AreEqual(before.X, after.X, 0.01f);
                Assert.AreEqual(before.Y, after.Y, 0.01f);

                view.SendContextMenu(view.PointToScreen(end));
                Assert.AreEqual(1, host.ContextMenuOpeningCount, "停用自由平移時右鍵拖曳不應吃掉右鍵功能表");
            }
        }

        [TestMethod]
        public void RightHoldWheel_ZoomsAtCursor_HandlesWheel_AndSuppressesContextMenu()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                var anchor = CenterOfFirstNode(view);
                var nodeClicks = 0;
                view.NodeMouseClick += (s, e) => nodeClicks++;

                var delta = SystemInformation.MouseWheelScrollDelta;
                view.RightDown(anchor);
                var zoomIn = view.Wheel(anchor, delta);
                Assert.IsTrue(zoomIn.Handled, "按住右鍵滾輪縮放應標記 Handled，不捲動捲軸");
                Assert.AreEqual(110, view.ZoomPercent);

                var zoomOut = view.Wheel(anchor, -delta * 2);
                Assert.IsTrue(zoomOut.Handled);
                Assert.AreEqual(90, view.ZoomPercent);

                view.RightUp(anchor);
                Assert.AreEqual(0, nodeClicks, "按住右鍵縮放後放開不觸發 NodeMouseClick");

                view.SendContextMenu(view.PointToScreen(anchor));
                Assert.AreEqual(0, host.ContextMenuOpeningCount, "按住右鍵縮放後放開不開啟右鍵功能表");

                // 放開右鍵後再滾輪應交回一般捲動，不再縮放。
                var plainWheel = view.Wheel(anchor, delta);
                Assert.IsFalse(plainWheel.Handled);
                Assert.AreEqual(90, view.ZoomPercent);
            }
        }

        [TestMethod]
        public void RightHoldWheel_StillZooms_WhenAllowFreePanIsFalse()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                view.AllowFreePan = false;
                var anchor = CenterOfFirstNode(view);

                view.RightDown(anchor);
                var args = view.Wheel(anchor, SystemInformation.MouseWheelScrollDelta);
                Assert.IsTrue(args.Handled);
                Assert.AreEqual(110, view.ZoomPercent);
                view.RightUp(anchor);

                view.SendContextMenu(view.PointToScreen(anchor));
                Assert.AreEqual(0, host.ContextMenuOpeningCount);
            }
        }

        [TestMethod]
        public void RightHold_CombiningWheelAndDrag_DoesNotJumpCanvas()
        {
            using (var view = CreateView())
            {
                var start = CenterOfFirstNode(view);
                var delta = SystemInformation.MouseWheelScrollDelta;

                // 先右鍵拖曳，再於拖曳中滾輪縮放，接著繼續拖曳：縮放後的位移應從縮放後位置累加。
                view.RightDown(start);
                var mid = new Point(start.X + 40, start.Y + 20);
                view.RightMove(mid);
                view.Wheel(mid, delta);
                Assert.AreEqual(110, view.ZoomPercent);

                var afterZoomOffset = view.Offset();
                var end = new Point(mid.X + 30, mid.Y - 15);
                view.RightMove(end);
                view.RightUp(end);

                var finalOffset = view.Offset();
                Assert.AreEqual(afterZoomOffset.X + 30, finalOffset.X, 1f);
                Assert.AreEqual(afterZoomOffset.Y - 15, finalOffset.Y, 1f);

                // 先按住右鍵滾輪縮放，再拖曳：拖曳應從縮放後位置平滑移動。
                view.RightDown(end);
                view.Wheel(end, -delta);
                Assert.AreEqual(100, view.ZoomPercent);

                var postZoomOffset = view.Offset();
                var next = new Point(end.X - 25, end.Y + 15);
                view.RightMove(next);
                view.RightUp(next);

                var nextOffset = view.Offset();
                Assert.AreEqual(postZoomOffset.X - 25, nextOffset.X, 1f);
                Assert.AreEqual(postZoomOffset.Y + 15, nextOffset.Y, 1f);
            }
        }

        [TestMethod]
        public void RightDrag_RealRButtonUp_SuppressesMouseContextMenu_ButAllowsKeyboardAndNextClick()
        {
            using (var host = new HostedFixture())
            {
                var view = host.View;
                var start = CenterOfFirstNode(view);
                var end = new Point(start.X + 50, start.Y + 20);

                // 模擬真實 Win32 WM_RBUTTONUP -> DefWndProc 同步送出 WM_CONTEXTMENU 的流程。
                view.RightDown(start);
                view.RightMove(end);
                view.SendRButtonUp(end);
                Assert.AreEqual(0, host.ContextMenuOpeningCount, "右鍵拖曳後的真實 WM_RBUTTONUP 不應開啟右鍵功能表");

                // 鍵盤開啟右鍵功能表（lParam == -1）不受影響。
                view.SendKeyboardContextMenu();
                Assert.AreEqual(1, host.ContextMenuOpeningCount, "鍵盤觸發的 WM_CONTEXTMENU 應照常開啟");

                // 下一次單純右鍵點擊不受影響。
                view.RightDown(start);
                view.SendRButtonUp(start);
                Assert.AreEqual(2, host.ContextMenuOpeningCount, "後續正常右鍵點擊應照常開啟右鍵功能表");
            }
        }

        private sealed class HostedFixture : IDisposable
        {
            public HostedFixture()
            {
                Form = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-2000, -2000),
                    ClientSize = new Size(600, 400),
                    ShowInTaskbar = false,
                };
                View = CreateView();
                View.Dock = DockStyle.Fill;
                View.TreeContextMenu.Opening += (s, e) =>
                {
                    ContextMenuOpeningCount++;
                    e.Cancel = true;
                };
                Form.Controls.Add(View);
                Form.Show();
            }

            public Form Form { get; }

            public FreePanProbeView View { get; }

            public int ContextMenuOpeningCount { get; private set; }

            public void Dispose()
            {
                View.Dispose();
                Form.Dispose();
            }
        }

        private static FreePanProbeView CreateView()
        {
            var tree = new MenuTree();
            tree.AddPath(new[] { "A" });
            tree.AddPath(new[] { "B" });

            var view = new FreePanProbeView { Size = new Size(600, 400) };
            view.SetTree(tree);
            return view;
        }

        private static Point CenterOfFirstNode(CascadeDiagramView view)
        {
            for (var y = 0; y < view.ClientSize.Height; y++)
            {
                for (var x = 0; x < view.ClientSize.Width; x++)
                {
                    if (view.GetNodeAt(new Point(x, y)) != null) return new Point(x + 2, y + 2);
                }
            }

            Assert.Fail("圖上沒有節點");
            return Point.Empty;
        }

        private sealed class FreePanProbeView : CascadeDiagramView
        {
            private const int WM_CONTEXTMENU = 0x007B;
            private const int WM_RBUTTONUP = 0x0205;

            public bool PanKeyDown { get; set; }

            internal override bool IsPanKeyDown => PanKeyDown;

            public PointF Offset()
            {
                GetTransform(out _, out var offset);
                return offset;
            }

            public void Down(Point location) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0));

            public void Move(Point location) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, location.X, location.Y, 0));

            public void Up(Point location)
            {
                var args = new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0);
                OnMouseClick(args);
                OnMouseUp(args);
            }

            public void RightDown(Point location) => OnMouseDown(new MouseEventArgs(MouseButtons.Right, 1, location.X, location.Y, 0));

            public void RightMove(Point location) => OnMouseMove(new MouseEventArgs(MouseButtons.Right, 0, location.X, location.Y, 0));

            public void RightUp(Point location)
            {
                var args = new MouseEventArgs(MouseButtons.Right, 1, location.X, location.Y, 0);
                OnMouseClick(args);
                OnMouseUp(args);
            }

            public HandledMouseEventArgs Wheel(Point location, int delta)
            {
                var args = new HandledMouseEventArgs(MouseButtons.None, 0, location.X, location.Y, delta);
                OnMouseWheel(args);
                return args;
            }

            public void SendRButtonUp(Point clientLocation)
            {
                var msg = Message.Create(Handle, WM_RBUTTONUP, IntPtr.Zero, MakeLParam(clientLocation.X, clientLocation.Y));
                WndProc(ref msg);
            }

            public void SendContextMenu(Point screenLocation)
            {
                var msg = Message.Create(Handle, WM_CONTEXTMENU, Handle, MakeLParam(screenLocation.X, screenLocation.Y));
                WndProc(ref msg);
            }

            public void SendKeyboardContextMenu()
            {
                var msg = Message.Create(Handle, WM_CONTEXTMENU, Handle, (IntPtr)(-1));
                WndProc(ref msg);
            }

            public bool Escape()
            {
                var msg = new Message();
                return ProcessCmdKey(ref msg, Keys.Escape);
            }

            private static IntPtr MakeLParam(int low, int high) =>
                (IntPtr)(((high & 0xFFFF) << 16) | (low & 0xFFFF));
        }
    }
}
