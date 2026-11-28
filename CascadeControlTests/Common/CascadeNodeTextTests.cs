using CascadeControl.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CascadeControlTests.Common
{
    [TestClass]
    public class CascadeNodeTextTests
    {
        [TestMethod]
        public void Format_PadsNumberAndMarksChildren()
        {
            Assert.AreEqual("<01>A", CascadeNodeText.Format(1, "A", hasChildren: false));
            Assert.AreEqual("<12>B [+]", CascadeNodeText.Format(12, "B", hasChildren: true));
        }

        [TestMethod]
        public void TryGetNumber_ReadsWhatFormatWrites()
        {
            Assert.IsTrue(CascadeNodeText.TryGetNumber(CascadeNodeText.Format(7, "A", hasChildren: true), out var number));
            Assert.AreEqual(7, number);
        }

        [TestMethod]
        public void TryGetNumber_NoTag_ReturnsFalse()
        {
            Assert.IsFalse(CascadeNodeText.TryGetNumber("A <01>", out _));
            Assert.IsFalse(CascadeNodeText.TryGetNumber(null, out _));
        }

        [TestMethod]
        public void StripNumber_RemovesLeadingTagOnly()
        {
            Assert.AreEqual("A", CascadeNodeText.StripNumber(" <01> A"));
            Assert.AreEqual("A <01>", CascadeNodeText.StripNumber("A <01>"));
            Assert.AreEqual(string.Empty, CascadeNodeText.StripNumber(null));
        }
    }
}
