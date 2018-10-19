using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp10
{
    [MemoryDiagnoser]
    //[SimpleJob(launchCount: 1, warmupCount: 1, targetCount: 1)]
    [ShortRunJob]
    public class Benchmark
    {
        [Params(100_000_000, 1_000_000, 10_000, 1_000)]
        public int MaxObjects { get; set; }

        [Params(1_000_000, 100_000, 1_000, 100)]
        public int MaxUniqObjects { get; set; }

        int[] data;

        ObjectPool<Counter> pool;

        [GlobalSetup]
        public void BenchmarkSetup()
        {
            data = new int[MaxObjects];
            var random = new Random(42);
            for (var i = 0; i < MaxObjects; i++)
            {
                data[i] = random.Next(MaxUniqObjects);
            }

            pool = new ObjectPool<Counter>(() => new Counter(), MaxUniqObjects);
        }

        [Benchmark]
        public void Dictionary()
        {
            var counters = new Dictionary<int, int>();

            foreach (var obj in data)
            {
                if (counters.TryGetValue(obj, out var count))
                {
                    counters[obj] = count + 1;
                }
                else
                {
                    counters[obj] = 1;
                }
            }
        }

        [Benchmark(Baseline = true)]
        public void Array()
        {
            var counters = new int[MaxUniqObjects];

            foreach (var obj in data)
            {
                //Thread.Sleep(10);
                counters[obj]++;
            }
        }

        [Benchmark]
        public void InterlockedArray()
        {
            var counters = new int[MaxUniqObjects];

            Parallel.ForEach(data, new ParallelOptions { MaxDegreeOfParallelism = 6 },
                obj => {
                    //Thread.Sleep(10);
                    Interlocked.Increment(ref counters[obj]);
                });
        }

        public class Counter
        {
            public int Count;
        }

        [Benchmark]
        public void InterlockedDictionary()
        {
            var counters = new ConcurrentDictionary<int, Counter>();
            Parallel.ForEach(data, new ParallelOptions { MaxDegreeOfParallelism = 6 },
                obj => {
                    Interlocked.Increment(ref counters.GetOrAdd(obj, i => pool.GetObject()).Count);
                });

            Parallel.ForEach(counters, new ParallelOptions { MaxDegreeOfParallelism = 6 },
                counter =>
                {
                    counter.Value.Count = 0;
                    pool.PutObject(counter.Value);
                });
        }

        [Benchmark]
        public void MergeDictionary()
        {
            var result = new Dictionary<int, int>();
            var resultLock = new object();
            Parallel.ForEach(data, new ParallelOptions { MaxDegreeOfParallelism = 6 }, () => new Dictionary<int, int>(),
                (obj, loopState, counters) => {
                    if (counters.TryGetValue(obj, out var count))
                    {
                        counters[obj] = count + 1;
                    }
                    else
                    {
                        counters[obj] = 1;
                    }
                    return counters;
                }, counters => {
                    lock(resultLock)
                    {
                        foreach (var counter in counters)
                        {
                            if (result.TryGetValue(counter.Key, out var count))
                            {
                                result[counter.Key] = count + counter.Value;
                            }
                            else
                            {
                                result[counter.Key] = counter.Value;
                            }
                        }
                    }
                });
        }

        [Benchmark]
        public void MergeArray()
        {
            var result = new int[MaxUniqObjects];
            var resultLock = new object();
            Parallel.ForEach(data, new ParallelOptions { MaxDegreeOfParallelism = 6 }, () => new int[MaxUniqObjects],
                (obj, loopState, counters) => {

                    counters[obj]++;
                    //Thread.Sleep(10);
                    
                    return counters;
                }, counters => {
                    lock (resultLock)
                    {
                        for (var i = 0; i < MaxUniqObjects; i++)
                        {
                            result[i] = result[i] + counters[i];
                        }
                    }
                });
        }


    }

    public class ObjectPool<T>
    {
        private ConcurrentBag<T> _objects;
        private Func<T> _objectGenerator;
        private int _maxPooledObjects;

        public ObjectPool(Func<T> objectGenerator, int maxPooledObjects)
        {
            if (objectGenerator == null)
                throw new ArgumentNullException("objectGenerator");

            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator;
            _maxPooledObjects = maxPooledObjects;
        }

        public T GetObject()
        {
            T item;
            if (_objects.TryTake(out item)) return item;
            return _objectGenerator();
        }

        public void PutObject(T item)
        {
            if (_objects.Count < _maxPooledObjects)
            {
                _objects.Add(item);
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var objectPool = new ConcurrentBag<Benchmark.Counter>();
            for(int i = 0; i < 128_000_000; ++i)
            {
                objectPool.Add(new Benchmark.Counter());
            }

            var summary = BenchmarkRunner.Run<Benchmark>();
            Console.ReadLine();
        }
    }
}
