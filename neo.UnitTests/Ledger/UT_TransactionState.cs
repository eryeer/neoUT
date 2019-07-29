using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Ledger;

namespace Neo.UnitTests.Ledger
{
    [TestClass]
    public class UT_TransactionState
    {
        [TestMethod]
        public void TestConstructor()
        {
            TransactionState state = new TransactionState();
            state.Should().NotBeNull();
        }
    }
}
