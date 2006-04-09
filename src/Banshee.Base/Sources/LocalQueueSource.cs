
/***************************************************************************
 *  LibrarySource.cs
 *
 *  Copyright (C) 2005 Novell
 *  Written by Aaron Bockover (aaron@aaronbock.net)
 ****************************************************************************/

/*  THIS FILE IS LICENSED UNDER THE MIT LICENSE AS OUTLINED IMMEDIATELY BELOW: 
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a
 *  copy of this software and associated documentation files (the "Software"),  
 *  to deal in the Software without restriction, including without limitation  
 *  the rights to use, copy, modify, merge, publish, distribute, sublicense,  
 *  and/or sell copies of the Software, and to permit persons to whom the  
 *  Software is furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in 
 *  all copies or substantial portions of the Software.
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
 *  FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
 *  DEALINGS IN THE SOFTWARE.
 */
 
using System;
using System.Collections;
using Mono.Unix;

using Banshee.Base;

namespace Banshee.Sources
{
    public class LocalQueueSource : Source
    {
        private ArrayList tracks = new ArrayList();
        
        private static LocalQueueSource instance;
        public static LocalQueueSource Instance {
            get {
                if(instance == null) {
                    instance = new LocalQueueSource();
                }
                
                return instance;
            }
        }
        
        private LocalQueueSource() : base(Catalog.GetString("Local Queue"), 100)
        {
            foreach(string file in Globals.ArgumentQueue.Files) {
                try {
                    SafeUri uri;
                    try {
                        uri = new SafeUri(file);
                    } catch(ApplicationException) {
                        uri = new SafeUri(System.Environment.CurrentDirectory + 
                            System.IO.Path.DirectorySeparatorChar + file);
                    }
                    
                    if(System.IO.File.Exists(uri.LocalPath)) {
                        tracks.Add(new FileTrackInfo(uri));   
                    } else {
                        throw new ApplicationException(uri.LocalPath);
                    }
                } catch(Exception e) {
                    Console.WriteLine("Could not load: {0}", e.Message);
                }
            }
        }
        
        public override int Count {
            get {
                return tracks.Count;
            }
        }
        
        public override IEnumerable Tracks {
            get {
                return tracks;
            }
        }

        public override Gdk.Pixbuf Icon {
            get {
                return IconThemeUtils.LoadIcon(22, "system-file-manager", "source-localqueue");
            }
        }
    }
}
