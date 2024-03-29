﻿using System;
using System.Configuration;
using System.Threading;
using Enyim.Caching;
using Enyim.Caching.Memcached;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Chuye.Caching.Memcached {

    public class MemcachedCacheProvider : CacheProvider, IHttpRuntimeCacheProvider, IRegion, IDistributedLock {
        private static MemcachedCacheProvider _default;
        private readonly MemcachedClient _client;

        public static MemcachedCacheProvider Default {
            get {
                if (_default != null) {
                    return _default;
                }

                var defaultInstance = new MemcachedCacheProvider("enyim.com/memcached", null);
                Interlocked.CompareExchange(ref _default, defaultInstance, null);
                return _default;
            }
        }

        public String Region { get; private set; }

        public MemcachedCacheProvider(String configSection, String region = null) {
            _client = new MemcachedClient("enyim.com/memcached");
            Region = region;
        }

        protected override String BuildCacheKey(String key) {
            return Region == null ? key : String.Concat(Region, "_", key);
        }

        // Will not last expire time
        public Boolean TryGetObject(string key, out object entry) {
            return _client.TryGet(BuildCacheKey(key), out entry);
        }

        public override bool TryGet<T>(string key, out T entry) {
            Object cacheEntry;
            Boolean exist = TryGetObject(key, out cacheEntry);
            if (!exist) {
                //不存在
                entry = default(T);
                return false;
            }
            if (cacheEntry == null) {
                //存在但为 null
                entry = (T)((Object)null);
                return true;
            }
            if (cacheEntry is T) {
                //存在，直接返回
                entry = (T)cacheEntry;
                return true;
            }

            //使用与不使用 NewtonsoftJsonTranscoder 的情况下都支持
            SlidingCacheWrapper<T> slidingCache;
            if (SlidingCacheWrapper<T>.IsSlidingCache(cacheEntry, out slidingCache)) {
                //尝试以 SlidingCacheWrapper<T> 处理
                var diffSpan = DateTime.Now.Subtract(slidingCache.SettingTime);
                //当前时间-设置时间>滑动时间, 已经过期
                if (diffSpan > slidingCache.SlidingExpiration) {
                    Expire(key);
                    entry = default(T);
                    return false;
                }

                //当前时间-设置时间> 滑动时间/2, 更新缓存
                if (diffSpan.Add(diffSpan) > slidingCache.SlidingExpiration) {
                    Overwrite(key, slidingCache.Value, slidingCache.SlidingExpiration);
                }
                entry = slidingCache.Value;
            }
            else {
                //尝试以普通JSON处理
                entry = NewtonsoftJsonUtil.EnsureObjectType<T>(cacheEntry);
            }
            return true;
        }

        public T GetOrCreate<T>(String key, Func<T> function, TimeSpan slidingExpiration) {
            T value;
            if (TryGet<T>(key, out value)) {
                return value;
            }
            value = function();
            Overwrite(key, value, slidingExpiration);
            return value;
        }

        public T GetOrCreate<T>(String key, Func<T> function, DateTime absoluteExpiration) {
            T value;
            if (TryGet<T>(key, out value)) {
                return value;
            }
            value = function();
            Overwrite(key, value, absoluteExpiration);
            return value;
        }

        public override void Overwrite<T>(String key, T value) {
            _client.Store(StoreMode.Set, BuildCacheKey(key), value);
        }

        //slidingExpiration 时间内无访问则过期
        public void Overwrite<T>(String key, T value, TimeSpan slidingExpiration) {
            //_client.Store(StoreMode.Set, BuildCacheKey(key), value, slidingExpiration);
            var cacheWraper = new SlidingCacheWrapper<T>(value, slidingExpiration);
            _client.Store(StoreMode.Set, BuildCacheKey(key), cacheWraper,
                TimeSpan.FromSeconds(slidingExpiration.TotalSeconds * 1.5));
        }

        //absoluteExpiration UTC或本地时间均可
        public void Overwrite<T>(String key, T value, DateTime absoluteExpiration) {
            _client.Store(StoreMode.Set, BuildCacheKey(key), value, absoluteExpiration);
        }

        public override void Expire(String key) {
            _client.Remove(BuildCacheKey(key));  // Could check result
        }

        public IDisposable ReleasableLock(String key, Int32 expire = DistributedLockTime.DisposeMillisecond) {
            while (!TryLock(key, expire)) {
                Thread.Sleep(DistributedLockTime.IntervalMillisecond);
            }
            return new MemcachedLockReleaser(_client, key);
        }

        public void Lock(String key, Int32 expire) {
            while (!TryLock(key, expire)) {
                Thread.Sleep(DistributedLockTime.IntervalMillisecond);
            }
        }

        public Boolean TryLock(String key, Int32 expire) {
            var result = _client.ExecuteStore(StoreMode.Add, BuildCacheKey(key), 1, TimeSpan.FromMilliseconds(expire));
            return result.Success;
        }

        public void UnLock(String key) {
            Expire(BuildCacheKey(key));
        }

        private struct MemcachedLockReleaser : IDisposable {
            private MemcachedClient _client;
            private String _key;

            public MemcachedLockReleaser(MemcachedClient client, String key) {
                _client = client;
                _key = key;
            }

            public void Dispose() {
                _client.ExecuteRemove(_key);
            }
        }

        [Serializable]
        public class SlidingCacheWrapper<T> {
            private const String SlidingExpirationProp = "0091081c219a456982dc7c881cce70c1";
            private const String SettingTimeProp = "fb7ec3ccf9764c7f9c4d4abe0878286b";

            public T Value { get; private set; }
            [JsonProperty(SlidingExpirationProp)]
            public TimeSpan SlidingExpiration { get; private set; }
            [JsonProperty(SettingTimeProp)]
            public DateTime SettingTime { get; set; }

            public SlidingCacheWrapper(T value, TimeSpan slidingExpiration) {
                Value = value;
                SlidingExpiration = slidingExpiration;
                SettingTime = DateTime.Now;
            }

            public static Boolean IsSlidingCache(Object obj, out SlidingCacheWrapper<T> cacheEntry) {
                cacheEntry = null;
                if (obj is SlidingCacheWrapper<T>) {
                    cacheEntry = (SlidingCacheWrapper<T>)obj;
                    return true;
                }
                if (obj is JObject) {
                    var jobj = (JObject)obj;
                    if (jobj.Property(SlidingExpirationProp) != null && jobj.Property(SettingTimeProp) != null) {
                        cacheEntry = jobj.ToObject<SlidingCacheWrapper<T>>();
                        return true;
                    }
                }
                return false;
            }
        }

        internal class NewtonsoftJsonUtil {
            public static T EnsureObjectType<T>(Object obj) {
                if (obj is T) {
                    return (T)obj;
                }
                else if (obj is JObject) {
                    return ((JObject)obj).ToObject<T>();
                }
                else {
                    //return (T)Convert.ChangeType(obj, typeof(T));  // Guid 类型将失败
                    return JToken.FromObject(obj).ToObject<T>();
                }
            }
        }
    }
}
