using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Ledger;

namespace Neo.UnitTests.Ledger
{
    [TestClass]
    public class UT_HeaderHashList
    {
        [TestMethod]
        public void TestConstructor()
        {
            HeaderHashList list = new HeaderHashList();
            list.Should().NotBeNull();
        }
    }
}
