using CascadeControl.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CascadeControlTests.Views
{
    [TestClass]
    public class CascadeNodeTextTests
    {
        [TestMethod]
        public void Number_PadsToDigitsOfSiblingCount()
        {
            Assert.AreEqual("<3>", CascadeNodeText.Number(3, 5));
            Assert.AreEqual("<03>", CascadeNodeText.Number(3, 12));
            Assert.AreEqual("<12>", CascadeNodeText.Number(12, 12));
            Assert.AreEqual("<003>", CascadeNodeText.Number(3, 100));
        }

        [TestMethod]
        public void Name_ChildCountWithoutChecks()
        {
            Assert.AreEqual("A (3)", CascadeNodeText.Name("A", showChildCount: true, childCount: 3, checkedChildCount: null));
        }

        [TestMethod]
        public void Name_ChildCountWithChecks()
        {
            Assert.AreEqual("A (1/3)", CascadeNodeText.Name("A", showChildCount: true, childCount: 3, checkedChildCount: 1));
            Assert.AreEqual("A (0/3)", CascadeNodeText.Name("A", showChildCount: true, childCount: 3, checkedChildCount: 0));
        }

        [TestMethod]
        public void Name_NoChildrenOrHidden_IsPlainName()
        {
            Assert.AreEqual("A", CascadeNodeText.Name("A", showChildCount: true, childCount: 0, checkedChildCount: 0));
            Assert.AreEqual("A", CascadeNodeText.Name("A", showChildCount: false, childCount: 3, checkedChildCount: 1));
        }
    }
}