// 
// MiroGuideImageFetchJob.cs
//  
// Authors:
//   Mike Urbanski <michael.c.urbanski@gmail.com>
//   Gabriel Burt <gburt@novell.com>
//
// Copyright (C) 2009 Michael C. Urbanski
// Copyright (C) 2008 Novell, Inc.
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

using Hyena;

using Banshee.Base;
using Banshee.Metadata;
using Banshee.ServiceStack;

using Banshee.Paas.MiroGuide.Gui;
using Banshee.Paas.Aether.MiroGuide;

// This could be abstracted and shared with PaasImageFetchJob...

namespace Banshee.Paas.MiroGuide
{
    public class MiroGuideImageFetchJob : MetadataServiceJob
    {
        private MiroGuideChannelInfo channel;
        
        public MiroGuideImageFetchJob (MiroGuideChannelInfo channel) : base ()
        {
            this.channel = channel;
        }
        
        public override void Run ()
        {
            Fetch ();
        }
        
        public void Fetch ()
        {            
            if (String.IsNullOrEmpty (channel.ThumbUrl)) {
                return;
            }
            
            string cover_art_id = PaasService.ArtworkIdFor (channel.Name);
            
            if (cover_art_id == null) {
                return;
            } else if (CoverArtSpec.CoverExists (cover_art_id)) {
                return;
            } else if (!InternetConnected) {
                return;
            }
            
            if (SaveHttpStreamCover (new Uri (channel.ThumbUrl), cover_art_id, null)) {
                Banshee.Sources.Source src = ServiceManager.SourceManager.ActiveSource;
                
                if (src != null && (src is TestSource || src.Parent is TestSource)) {
                    (src as TestSource).QueueDraw ();
                }
                
                return;
            }
        }
    }
}