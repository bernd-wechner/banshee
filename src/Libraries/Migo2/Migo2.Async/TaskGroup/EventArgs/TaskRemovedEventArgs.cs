// 
// TaskRemovedEventArgs.cs
//  
// Author:
//       Mike Urbanski <michael.c.urbanski@gmail.com>
// 
// Copyright (c) 2009 Michael C. Urbanski
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;

using Migo2.Collections;

namespace Migo2.Async
{
    public class TaskRemovedEventArgs<T> : ManipulatedEventArgs<T> where T : Task 
    {  
        private readonly int index;
        private readonly IEnumerable<Pair<int,int>> indices;

        public int Index
        {
            get { return index; }
        }
        
        // All indices are listed in descending order from the end of the 
        // list so that in order removal will not affect indices.
   
        // int - 0:  Index
        // int - 1:  Count
        public IEnumerable <Pair<int,int>> Indices
        {
            get { return indices; }
        }

        protected TaskRemovedEventArgs (int index, T task, 
                                        IEnumerable<Pair<int,int>> indices, 
                                        ICollection<T> tasks)
            : base (task, tasks)
        {
            if (indices == null && index < 0) {
                throw new InvalidOperationException (
                    "indices may not be null if index is < 0"
                );
            }
            
            this.index = index;
            this.indices = indices;
        }
        
        public TaskRemovedEventArgs (int index, T task) 
            : this (index, task, null, null) 
        {
        }
        
        public TaskRemovedEventArgs (IEnumerable<Pair<int,int>> indices, ICollection<T> tasks)
            : this (-1, null, indices, tasks) 
        {
        }
    }
}
