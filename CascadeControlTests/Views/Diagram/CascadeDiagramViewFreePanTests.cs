using CascadeControl.Trees;
using CascadeControl.Views.Diagram;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Diagram
{
    /// <summary>
    /// <see cref="CascadeDiagramView.AllowFreePan"/>：Shift + 左鍵拖曳自由平移、Esc 還原、關閉時回到捲軸版行為。
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

            public bool Escape()
            {
                var msg = new Message();
                return ProcessCmdKey(ref msg, Keys.Escape);
            }
        }
    }
}
