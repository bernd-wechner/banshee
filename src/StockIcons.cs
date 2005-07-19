/***************************************************************************
 *  StockIcons.cs
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
using System.IO;
using Gtk;
using Gdk;

namespace Sonance
{
	public class StockIcons 
	{
		private static string [] iconList = {
			/* Playback Control Icons */
			"media-next",
			"media-prev",
			"media-play",
			"media-pause",
			"media-shuffle",
			"media-repeat",
			"media-eject",
			
			/* Volume Button Icons */
			"volume-max",
			"volume-med",
			"volume-min",
			"volume-zero",
			"volume-decrease",
			"volume-increase",
			
			/* Now Playing Images */
			"icon-artist",
			"icon-album",
			"icon-title"
		};	

		public static void Initialize()
		{
			IconFactory factory = new IconFactory();
			factory.AddDefault();

			foreach(string name in iconList) 
				factory.Add(name, new IconSet(
					Pixbuf.LoadFromResource(name + ".png")));
		}
	}
}
