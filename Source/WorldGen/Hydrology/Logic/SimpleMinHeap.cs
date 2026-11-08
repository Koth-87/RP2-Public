using System;

namespace Planets.WorldGen.Hydrology.Logic
{
    /// <summary>
    /// A lightweight binary min-heap optimized for the RefillMergedChunk frontier.
    /// Provides O(log N) insertion and extraction, replacing the O(N) linear scan.
    /// </summary>
    internal class SimpleMinHeap
    {
        private struct HeapEntry
        {
            public float Elevation;
            public int TileID;
            public int SourceTileID;
            public int SourceNeighborIndex;
        }
        
        private HeapEntry[] heap;
        private int count;
        
        public SimpleMinHeap(int initialCapacity = 64)
        {
            heap = new HeapEntry[initialCapacity];
            count = 0;
        }
        
        public int Count => count;
        
        public void Insert(float elevation, int tileID, int sourceTileID, int sourceNeighborIndex)
        {
            // Grow array if needed
            if (count == heap.Length)
            {
                Array.Resize(ref heap, heap.Length * 2);
            }
            
            // Add to end and bubble up
            heap[count] = new HeapEntry 
            { 
                Elevation = elevation, 
                TileID = tileID, 
                SourceTileID = sourceTileID, 
                SourceNeighborIndex = sourceNeighborIndex 
            };
            BubbleUp(count);
            count++;
        }
        
        public (float elevation, int tileID, int sourceTileID, int sourceNeighborIndex) ExtractMin()
        {
            if (count == 0) throw new InvalidOperationException("Heap is empty");
            
            var min = heap[0];
            count--;
            
            if (count > 0)
            {
                heap[0] = heap[count];
                BubbleDown(0);
            }
            
            return (min.Elevation, min.TileID, min.SourceTileID, min.SourceNeighborIndex);
        }
        
        public void Clear()
        {
            count = 0;
        }

        private static bool IsHigherPriority(in HeapEntry a, in HeapEntry b)
        {
            if (a.Elevation != b.Elevation)
                return a.Elevation < b.Elevation;
            return a.TileID < b.TileID;
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                
                if (IsHigherPriority(in heap[index], in heap[parent]))
                {
                    Swap(index, parent);
                    index = parent;
                }
                else
                {
                    break;
                }
            }
        }

        private void BubbleDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = index * 2 + 2;
                int smallest = index;
                
                if (left < count && IsHigherPriority(in heap[left], in heap[smallest]))
                {
                    smallest = left;
                }
                
                if (right < count && IsHigherPriority(in heap[right], in heap[smallest]))
                {
                    smallest = right;
                }
                
                if (smallest != index)
                {
                    Swap(index, smallest);
                    index = smallest;
                }
                else
                {
                    break;
                }
            }
        }
        
        private void Swap(int a, int b)
        {
            var temp = heap[a];
            heap[a] = heap[b];
            heap[b] = temp;
        }
    }
}
