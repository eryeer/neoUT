using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Wallets.SQLite;
using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Threading;

namespace Neo.UnitTests.Wallets.SQLite
{
    [TestClass]
    public class UT_UserWallet
    {
        private string path;
        private UserWallet wallet;
        public static string GetRandomPath()
        {
            string threadName = Thread.CurrentThread.ManagedThreadId.ToString();
            return Path.GetFullPath(string.Format("Wallet_{0}", new Random().Next(1, 1000000).ToString("X8")) + threadName);
        }

        [TestInitialize]
        public void Setup()
        {
            path = GetRandomPath();
            wallet = UserWallet.Create(path, "123456");
        }

        [TestCleanup]
        public void Cleanup()
        {
            TestUtils.DeleteFile(path);
        }

        [TestMethod]
        public void TestGetName()
        {
            wallet.Name.Should().Be(Path.GetFileNameWithoutExtension(path));
        }

        [TestMethod]
        public void TestGetVersion()
        {
            Action action = () => wallet.Version.ToString();
            action.ShouldNotThrow();
        }

        [TestMethod]
        public void TestCreateAndOpenSecureString() {
            string myPath = GetRandomPath();
            var ss = new SecureString();
            ss.AppendChar('a');
            ss.AppendChar('b');
            ss.AppendChar('c');

            var w1 =  UserWallet.Create(myPath,ss);
            w1.Should().NotBeNull();

            var w2 = UserWallet.Open(myPath, ss);
            w2.Should().NotBeNull();

            ss.AppendChar('d');
            Action action = () =>UserWallet.Open(myPath, ss);
            action.ShouldThrow<CryptographicException>();

            TestUtils.DeleteFile(myPath);
        }

        [TestMethod]
        public void TestOpen()
        {
            var w1 = UserWallet.Open(path, "123456");
            w1.Should().NotBeNull();

            Action action = () => UserWallet.Open(path, "123");
            action.ShouldThrow<CryptographicException>();
        }

        [TestMethod]
        public void TestCreateAccountByPrivateKey() {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            var account = wallet.CreateAccount(privateKey);
        }
    }
}
