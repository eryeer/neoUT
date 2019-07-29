using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Ledger;

namespace Neo.UnitTests.Ledger
{
    [TestClass]
    public class UT_HashIndexState
    {
        [TestMethod]
        public void TestConstructor()
        {
            HashIndexState state = new HashIndexState();
            state.Should().NotBeNull();
        }
    }
}
