// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;
using UnityEngine.Pool;

namespace Datadog.Unity.Worker
{
    public class ThreadSafeObjectPool<T> : IDisposable, IObjectPool<T>
        where T : class
    {
        private readonly object _lock = new ();
        private readonly ObjectPool<T> _pool;

        public ThreadSafeObjectPool(
            Func<T> createFunc,
            Action<T> actionOnGet = null,
            Action<T> actionOnRelease = null,
            Action<T> actionOnDestroy = null,
            bool collectionCheck = true,
            int defaultCapacity = 10,
            int maxSize = 10000)
        {
            _pool = new (createFunc, actionOnGet, actionOnRelease, actionOnDestroy, collectionCheck, defaultCapacity,
                maxSize);
        }

        public int CountInactive
        {
            get
            {
                lock(_lock)
                {
                    return _pool.CountInactive;
                }
            }
        }

        public T Get()
        {
            lock (_lock)
            {
                return _pool.Get();
            }
        }

        public PooledObject<T> Get(out T v)
        {
            lock (_lock)
            {
                return _pool.Get(out v);
            }
        }

        public void Release(T element)
        {
            lock (_lock)
            {
                _pool.Release(element);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _pool.Clear();
            }
        }

        public void Dispose() => this.Clear();
    }
}
