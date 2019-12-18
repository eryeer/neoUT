using Neo.IO.Caching;
using Neo.Network.P2P.Payloads;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;


namespace Neo
{
    /// <summary>
    /// This class stores a 256 bit unsigned int, represented as a 32-byte little-endian byte array
    /// </summary>
    public class UInt256 : UIntBase, IComparable<UInt256>, IEquatable<UInt256>
    {
        public const int Length = 32;
        public static readonly UInt256 Zero = new UInt256();


        /// <summary>
        /// The empty constructor stores a null byte array
        /// </summary>
        public UInt256()
            : this(null)
        {
        }

        /// <summary>
        /// The byte[] constructor invokes base class UIntBase constructor for 32 bytes
        /// </summary>
        public UInt256(byte[] value)
            : base(Length, value)
        {
        }

        /// <summary>
        /// Method CompareTo returns 1 if this UInt256 is bigger than other UInt256; -1 if it's smaller; 0 if it's equals
        /// Example: assume this is 01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4, this.CompareTo(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) returns 1
        /// </summary>
        public unsafe int CompareTo(UInt256 other)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            fixed (byte* px = ToArray(), py = other.ToArray())
            {
                ulong* lpx = (ulong*)px;
                ulong* lpy = (ulong*)py;
                //256bit / 64bit(ulong step) -1
                for (int i = (256 / 64 - 1); i >= 0; i--)
                {
                    if (lpx[i] > lpy[i])
                        return 1;
                    if (lpx[i] < lpy[i])
                        return -1;
                }
            }
            stopwatch.Stop();
            //Console.WriteLine($"Uint256 timespan: {stopwatch.Elapsed.TotalMilliseconds}");
            stopwatch.Reset();
            return 0;
        }

        /// <summary>
        /// Method Equals returns true if objects are equal, false otherwise
        /// </summary>
        public unsafe bool Equals(UInt256 other)
        {
            if (other is null) return false;
            fixed (byte* px = ToArray(), py = other.ToArray())
            {
                ulong* lpx = (ulong*)px;
                ulong* lpy = (ulong*)py;
                //256bit / 64bit(ulong step) -1
                for (int i = (256 / 64 - 1); i >= 0; i--)
                {
                    if (lpx[i] != lpy[i])
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Method Parse receives a big-endian hex string and stores as a UInt256 little-endian 32-bytes array
        /// Example: Parse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff01") should create UInt256 01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4
        /// </summary>
        public static new UInt256 Parse(string s)
        {
            if (s == null)
                throw new ArgumentNullException();
            if (s.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                s = s.Substring(2);
            if (s.Length != Length * 2)
                throw new FormatException();
            return new UInt256(s.HexToBytes().Reverse().ToArray());
        }

        /// <summary>
        /// Method TryParse tries to parse a big-endian hex string and store it as a UInt256 little-endian 32-bytes array
        /// Example: TryParse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff01", result) should create result UInt256 01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4
        /// </summary>
        public static bool TryParse(string s, out UInt256 result)
        {
            if (s == null)
            {
                result = null;
                return false;
            }
            if (s.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                s = s.Substring(2);
            if (s.Length != Length * 2)
            {
                result = null;
                return false;
            }
            byte[] data = new byte[Length];
            for (int i = 0; i < Length; i++)
                if (!byte.TryParse(s.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, null, out data[i]))
                {
                    result = null;
                    return false;
                }
            result = new UInt256(data.Reverse().ToArray());
            return true;
        }

        /// <summary>
        /// Operator > returns true if left UInt256 is bigger than right UInt256
        /// Example: UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) > UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) is true
        /// </summary>
        public static bool operator >(UInt256 left, UInt256 right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Operator >= returns true if left UInt256 is bigger or equals to right UInt256
        /// Example: UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) >= UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) is true
        /// </summary>
        public static bool operator >=(UInt256 left, UInt256 right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <summary>
        /// Operator < returns true if left UInt256 is less than right UInt256
        /// Example: UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) < UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) is true
        /// </summary>
        public static bool operator <(UInt256 left, UInt256 right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Operator <= returns true if left UInt256 is less or equals to right UInt256
        /// Example: UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) <= UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) is true
        /// </summary>
        public static bool operator <=(UInt256 left, UInt256 right)
        {
            return left.CompareTo(right) <= 0;
        }

        static void Main01(string[] args)
        {
            Stopwatch stopwatch = new Stopwatch();
            var fifoSet = new HashSetCache<UInt256>(15_000);
            var hashes = new UInt256[10000];
            var index = 0;
            for (int i = 0; i < 135_000; i++)
            {
                var transaction = CreateRandomHashTransaction();
                fifoSet.Add(transaction.Hash);
                if (i % 5 == 0 && i <50000) hashes[index++] = transaction.Hash;
            }
            Console.WriteLine($"HashSetCache size: {fifoSet.Size}");
            stopwatch.Start();
            fifoSet.ExceptWith(hashes);
            stopwatch.Stop();
            Console.WriteLine($"timespan:{stopwatch.Elapsed.TotalSeconds}");
            Console.WriteLine($"HashSetCache size: {fifoSet.Size}");
        }

        public static readonly Random TestRandom = new Random(1337);

        public static Transaction CreateRandomHashTransaction()
        {
            var randomBytes = new byte[16];
            TestRandom.NextBytes(randomBytes);
            return new Transaction
            {
                Script = randomBytes,
                Sender = UInt160.Zero,
                Attributes = new TransactionAttribute[0],
                Cosigners = new Cosigner[0],
                Witnesses = new[]
                {
                    new Witness
                    {
                        InvocationScript = new byte[0],
                        VerificationScript = new byte[0]
                    }
                }
            };
        }
    }
}
