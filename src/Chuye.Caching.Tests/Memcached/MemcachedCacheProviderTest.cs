﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Chuye.Caching.Memcached;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chuye.Caching.Tests.Memcached {
    [TestClass]
    public class MemcachedCacheProviderTest {
        [TestMethod]
        public void GetOrCreateTest() {
            var key = Guid.NewGuid().ToString("n");
            var val = Guid.NewGuid();

            IHttpRuntimeCacheProvider cacheProvider = MemcachedCacheProvider.Default;
            var result = cacheProvider.GetOrCreate<Guid>(key, () => val);
            Assert.AreEqual(result, val);

            {
                var exist = cacheProvider.TryGet<Guid>(key, out val);
                Assert.IsTrue(exist);
                Assert.AreEqual(result, val);
            }

            {
                var result2 = cacheProvider.GetOrCreate<Guid>(key, () => {
                    Assert.Fail();
                    return Guid.NewGuid();
                });
                Assert.AreEqual(result2, val);
            }
        }

        [TestMethod]
        public void OverwriteTest() {
            var key = Guid.NewGuid().ToString("n");
            var val = Guid.NewGuid();

            IHttpRuntimeCacheProvider cacheProvider = MemcachedCacheProvider.Default;
            var result = cacheProvider.GetOrCreate<Guid>(key, () => val);
            Assert.AreEqual(result, val);

            var val2 = Guid.NewGuid();
            cacheProvider.Overwrite<Guid>(key, val2);

            Guid val3;
            var exist = cacheProvider.TryGet<Guid>(key, out val3);
            Assert.IsTrue(exist);
            Assert.AreEqual(val3, val2);
        }

        [TestMethod]
        public void OverwriteWithslidingExpirationTest() {
            var key = Guid.NewGuid().ToString("n");
            var val = Guid.NewGuid();

            IHttpRuntimeCacheProvider cacheProvider = MemcachedCacheProvider.Default;

            //DateTime.Now]
            Guid result;
            cacheProvider.Overwrite(key, val, TimeSpan.FromSeconds(3D));
            {
                var exist = cacheProvider.TryGet<Guid>(key, out result);
                Assert.IsTrue(exist);
                Assert.AreEqual(result, val);
            }
            {
                Thread.Sleep(TimeSpan.FromSeconds(5D));
                var exist = cacheProvider.TryGet<Guid>(key, out result);
                Assert.IsFalse(exist);
            }
        }

        [TestMethod]
        public void OverwriteWithAbsoluteExpirationTest() {
            var key = Guid.NewGuid().ToString("n");
            var val = Guid.NewGuid();

            IHttpRuntimeCacheProvider cacheProvider = MemcachedCacheProvider.Default;
            var t1 = DateTime.Now.AddSeconds(8D);
            var t2 = DateTime.UtcNow.AddSeconds(8D);
            Assert.AreEqual(t1.ToTimestamp(), t2.ToTimestamp());

            //DateTime.Now
            Guid result;
            cacheProvider.Overwrite(key, val, DateTime.Now.AddSeconds(3D));
            {
                var exist = cacheProvider.TryGet<Guid>(key, out result);
                Assert.IsTrue(exist);
                Assert.AreEqual(result, val);
            }
            {
                Thread.Sleep(TimeSpan.FromSeconds(5D));
                var exist = cacheProvider.TryGet<Guid>(key, out result);
                Assert.IsFalse(exist);
            }

            //DateTime.UtcNow
            cacheProvider.Overwrite(key, val, DateTime.UtcNow.AddSeconds(3D));
            {
                var exist = cacheProvider.TryGet<Guid>(key, out result);
                Assert.IsTrue(exist);
                Assert.AreEqual(result, val);
            }
            {
                Thread.Sleep(TimeSpan.FromSeconds(5D));
                var exist = cacheProvider.TryGet<Guid>(key, out result);
                Assert.IsFalse(exist);
            }
        }

        [TestMethod]
        public void ExpireTest() {
            var key = Guid.NewGuid().ToString("n");
            var val = Guid.NewGuid();

            IHttpRuntimeCacheProvider cacheProvider = MemcachedCacheProvider.Default;
            var result = cacheProvider.GetOrCreate<Guid>(key, () => val);
            Assert.AreEqual(result, val);

            var exist = cacheProvider.TryGet<Guid>(key, out val);
            Assert.IsTrue(exist);

            cacheProvider.Expire(key);
            Guid val2;
            exist = cacheProvider.TryGet<Guid>(key, out val2);
            Assert.IsFalse(exist);
            Assert.AreEqual(val2, Guid.Empty);
        }

        [TestMethod]
        public void DistributedLock() {
            IDistributedLock memcached = MemcachedCacheProvider.Default;
            var key = "DistributedLock1";
            
            {
                var list = new List<int>();
                var except = new Random().Next(1000, 2000);
                var stopwatch = Stopwatch.StartNew();

                Parallel.For(0, except, i => {
                    using (memcached.ReleasableLock(key)) {
                        list.Add(i);
                    }
                });
                stopwatch.Stop();
                Console.WriteLine("Handle {0} times cost {1}, {2:f2} per sec.",
                    except, stopwatch.Elapsed.TotalSeconds, except / stopwatch.Elapsed.TotalSeconds);

                Assert.AreEqual(list.Count, except);
            }

            {
                var list = new List<int>();
                var except = new Random().Next(1000, 2000);
                var stopwatch = Stopwatch.StartNew();

                Parallel.For(0, except, i => {
                    memcached.ReleasableLock(key);
                    list.Add(i);
                    memcached.UnLock(key);
                });

                stopwatch.Stop();
                Console.WriteLine("Handle {0} times cost {1}, {2:f2} per sec.",
                    except, stopwatch.Elapsed.TotalSeconds, except / stopwatch.Elapsed.TotalSeconds);

                Assert.AreEqual(list.Count, except);
            }
        }
    }

    public static class Util {
        //((DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks) / 10000000).Dump();
        //((DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000).Dump();
        //((Int64)(DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, DateTime.Now.Kind)).TotalSeconds).Dump();
        public static Int64 ToTimestamp(this DateTime time) {
            return (Int64)(time.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))).TotalSeconds;
        }

        public static DateTime FromTimestamp(this Int64 timestamp) {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp);
        }
    }
}

