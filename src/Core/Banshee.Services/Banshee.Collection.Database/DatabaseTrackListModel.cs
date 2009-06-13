//
// DatabaseTrackListModel.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//   Gabriel Burt <gburt@novell.com>
//
// Copyright (C) 2007 Novell, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Data;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using Mono.Unix;

using Hyena;
using Hyena.Data;
using Hyena.Data.Sqlite;
using Hyena.Query;

using Banshee.Base;
using Banshee.Query;
using Banshee.Database;
using Banshee.PlaybackController;

namespace Banshee.Collection.Database
{       
    public class DatabaseTrackListModel : TrackListModel, IExportableModel, 
        ICacheableDatabaseModel, IFilterable, ISortable, ICareAboutView
    {
        private readonly BansheeDbConnection connection;
        private IDatabaseTrackModelProvider provider;
        protected IDatabaseTrackModelCache cache;
        private Banshee.Sources.DatabaseSource source;
        
        private long count;

        private long filtered_count;
        private TimeSpan filtered_duration;
        private long filtered_filesize, filesize;
        
        private ISortableColumn sort_column;
        private string sort_query;
        private bool forced_sort_query;
        
        private string reload_fragment;
        private string join_table, join_fragment, join_primary_key, join_column, condition, condition_from;

        private string query_fragment;
        private string user_query;

        private int rows_in_view;
        
        public DatabaseTrackListModel (BansheeDbConnection connection, IDatabaseTrackModelProvider provider, Banshee.Sources.DatabaseSource source)
        {
            this.connection = connection;
            this.provider = provider;
            this.source = source;
        }
        
        protected HyenaSqliteConnection Connection {
            get { return connection; }
        }

        private bool initialized = false;
        public void Initialize (IDatabaseTrackModelCache cache)
        {
            if (initialized)
                return;

            initialized = true;
            this.cache = cache;
            cache.AggregatesUpdated += HandleCacheAggregatesUpdated;
            GenerateSortQueryPart ();
        }
        
        private bool have_new_user_query = true;
        private void GenerateUserQueryFragment ()
        {
            if (!have_new_user_query)
                return;

            if (String.IsNullOrEmpty (UserQuery)) {
                query_fragment = null;
                query_tree = null;
            } else {
                query_tree = UserQueryParser.Parse (UserQuery, BansheeQuery.FieldSet);
                query_fragment = (query_tree == null) ? null : query_tree.ToSql (BansheeQuery.FieldSet);

                if (query_fragment != null && query_fragment.Length == 0) {
                    query_fragment = null;
                    query_tree = null;
                }
            }

            have_new_user_query = false;
        }

        private QueryNode query_tree;
        public QueryNode Query {
            get { return query_tree; }
        }

        protected string SortQuery {
            get { return sort_query; }
            set { sort_query = value; }
        }

        protected virtual void GenerateSortQueryPart ()
        {
            SortQuery = (SortColumn == null || SortColumn.SortType == SortType.None)
                ? (SortColumn != null && source is Banshee.Playlist.PlaylistSource)
                    ? "CorePlaylistEntries.ViewOrder ASC, CorePlaylistEntries.EntryID ASC" 
                    : BansheeQuery.GetSort ("Artist", true)
                : BansheeQuery.GetSort (SortColumn.SortKey, SortColumn.SortType == SortType.Ascending);
        }

        private SortType last_sort_type = SortType.None;
        public bool Sort (ISortableColumn column)
        {
            lock (this) {
                if (forced_sort_query) {
                    return false;
                }

                // Don't sort by the same column and the same sort-type more than once
                if (sort_column != null && sort_column == column && column.SortType == last_sort_type) {
                    return false;
                }

                last_sort_type = column.SortType;
                sort_column = column;

                GenerateSortQueryPart ();
                cache.Clear ();
            }
            return true;
        }

        private void HandleCacheAggregatesUpdated (IDataReader reader)
        {
            filtered_duration = TimeSpan.FromMilliseconds (reader.IsDBNull (1) ? 0 : Convert.ToInt64 (reader[1]));
            filtered_filesize = reader.IsDBNull (2) ? 0 : Convert.ToInt64 (reader[2]);
        }
        
        public override void Clear ()
        {
            cache.Clear ();
            count = 0;
            filesize = 0;
            filtered_count = 0;
            OnCleared ();
        }

        public void InvalidateCache (bool notify)
        {
            if (cache == null) {
                Log.ErrorFormat ("Called invalidate cache for {0}'s track model, but cache is null", source);
            } else {
                cache.Clear ();
                if (notify) {
                    OnReloaded ();
                }
            }
        }

        private string unfiltered_query;
        public string UnfilteredQuery {
            get {
                return unfiltered_query ?? (unfiltered_query = String.Format (
                    "FROM {0} WHERE {1} {2}",
                    FromFragment,
                    String.IsNullOrEmpty (provider.Where) ? "1=1" : provider.Where,
                    ConditionFragment
                ));
            }
        }

        private string from;
        protected string From {
            get { return from ?? provider.From; }
            set { from = value; }
        }

        private string from_fragment;
        public string FromFragment {
            get { return from_fragment ?? (from_fragment = String.Format ("{0}{1}", From, JoinFragment)); }
        }

        public virtual void UpdateUnfilteredAggregates ()
        {
            HyenaSqliteCommand count_command = new HyenaSqliteCommand (String.Format (
                "SELECT COUNT(*), SUM(CoreTracks.FileSize) {0}", UnfilteredQuery
            ));
            
            using (HyenaDataReader reader = new HyenaDataReader (connection.Query (count_command))) {
                count = reader.Get<long> (0);
                filesize = reader.Get<long> (1);
            }
        }

        public override void Reload ()
        {
            Reload (null);
        }

        public void Reload (IListModel reloadTrigger)
        {
            if (cache == null) {
                Log.WarningFormat ("Called Reload on {0} for source {1} but cache is null;  Did you forget to call AfterInitialized () in your DatabaseSource ctor?",
                    this, source == null ? "null source!" : source.Name);
                return;
            }

            lock (this) {
                GenerateUserQueryFragment ();

                UpdateUnfilteredAggregates ();
                cache.SaveSelection ();

                List<IFilterListModel> reload_models = new List<IFilterListModel> ();
                bool found = (reloadTrigger == null);
                foreach (IFilterListModel filter in source.CurrentFilters) {
                    if (found) {
                        reload_models.Add (filter);
                    } else if (filter == reloadTrigger) {
                        found = true;
                    }
                }

                if (reload_models.Count == 0) {
                    ReloadWithFilters (true);
                } else {
                    ReloadWithoutFilters ();

                    foreach (IFilterListModel model in reload_models) {
                        model.Reload (false);
                    }
                    
                    bool have_filters = false;
                    foreach (IFilterListModel filter in source.CurrentFilters) {
                        have_filters |= !filter.Selection.AllSelected;
                    }
                    
                    // Unless both artist/album selections are "all" (eg unfiltered), reload
                    // the track model again with the artist/album filters now in place.
                    if (have_filters) {
                        ReloadWithFilters (true);
                    }
                }

                cache.UpdateAggregates ();
                cache.RestoreSelection ();

                filtered_count = cache.Count;

                OnReloaded ();

                // Trigger these after the track list, b/c visually it's more important for it to update first
                foreach (IFilterListModel model in reload_models) {
                    model.RaiseReloaded ();
                }
            }
        }

        private void ReloadWithoutFilters ()
        {
            ReloadWithFilters (false);
        }

        private void ReloadWithFilters (bool with_filters)
        {
            StringBuilder qb = new StringBuilder ();
            qb.Append (UnfilteredQuery);
            
            if (with_filters) {
                foreach (IFilterListModel filter in source.CurrentFilters) {
                    string filter_sql = filter.GetSqlFilter ();
                    if (filter_sql != null) {
                        qb.Append (" AND ");
                        qb.Append (filter_sql);
                    }
                }
            }
            
            if (query_fragment != null) {
                qb.Append (" AND ");
                qb.Append (query_fragment);
            }
            
            if (sort_query != null) {
                qb.Append (" ORDER BY ");
                qb.Append (sort_query);
            }
            
            reload_fragment = qb.ToString ();
            cache.Reload ();
        }

        public override int IndexOf (TrackInfo track)
        {
            lock (this) {
                if (track is DatabaseTrackInfo) {
                    return (int) cache.IndexOf (track as DatabaseTrackInfo);
                } else if (track is Banshee.Streaming.RadioTrackInfo) {
                    return (int) cache.IndexOf ((track as Banshee.Streaming.RadioTrackInfo).ParentTrack as DatabaseTrackInfo);
                }
                return -1;
            }
        }

        public int IndexOfFirst (TrackInfo track)
        {
            lock (this) {
                return IndexOf (cache.GetSingle ("AND MetadataHash = ? ORDER BY OrderID", track.MetadataHash));
            }
        }

#region Get random methods

        private const string random_condition = "AND LastStreamError = 0 AND (LastPlayedStamp < ? OR LastPlayedStamp IS NULL) AND (LastSkippedStamp < ? OR LastSkippedStamp IS NULL)";
        private static string random_fragment = String.Format ("{0} ORDER BY RANDOM()", random_condition);
        private static string random_by_album_fragment = String.Format ("AND CoreTracks.AlbumID = ? {0} ORDER BY Disc ASC, TrackNumber ASC", random_condition);
        private static string random_by_artist_fragment = String.Format ("AND CoreAlbums.ArtistID = ? {0} ORDER BY CoreTracks.Year, CoreTracks.AlbumID ASC, Disc ASC, TrackNumber ASC", random_condition);
        private static string random_by_rating_fragment = @"
            AND (LastPlayedStamp < ? OR LastPlayedStamp IS NULL)
            AND (LastSkippedStamp < ? OR LastSkippedStamp IS NULL)
            AND (CoreTracks.Rating = ? OR (? = 3 AND CoreTracks.Rating = 0))
            ORDER BY RANDOM()";

        private DateTime random_began_at = DateTime.MinValue;
        private DateTime last_random = DateTime.MinValue;
        private int? random_album_id;
        private int? random_artist_id;

        public override TrackInfo GetRandom (DateTime notPlayedSince, PlaybackShuffleMode mode, bool repeat, bool lastWasSkipped)
        {
            lock (this) {
                if (Count == 0) {
                    return null;
                }

                if (random_began_at < notPlayedSince) {
                    random_began_at = last_random = notPlayedSince;
                }

                TrackInfo track = GetRandomTrack (mode, repeat, lastWasSkipped);
                if (track == null && (repeat || mode != PlaybackShuffleMode.Linear)) {
                    random_began_at = (random_began_at == last_random) ? DateTime.Now : last_random;
                    random_album_id = random_artist_id = null;
                    track = GetRandomTrack (mode, repeat, lastWasSkipped);
                }

                last_random = DateTime.Now;
                return track;
            }
        }

        private TrackInfo GetRandomTrack (PlaybackShuffleMode mode, bool repeat, bool lastWasSkipped)
        {
            // Skip the entire album or artist
            if (lastWasSkipped) {
                random_album_id = random_artist_id = null;
            }

            if (mode == PlaybackShuffleMode.Album) {
                random_artist_id = null;
                if (random_album_id == null) {
                    random_album_id = GetRandomAlbumId (random_began_at);
                    if (random_album_id == null && repeat) {
                        random_began_at = last_random;
                        random_album_id = GetRandomAlbumId (random_began_at);
                    }
                }

                if (random_album_id != null) {
                    return cache.GetSingle (random_by_album_fragment, (int)random_album_id, random_began_at, random_began_at);
                }
                return null;
            } else if (mode == PlaybackShuffleMode.Artist) {
                random_album_id = null;
                if (random_artist_id == null) {
                    random_artist_id = GetRandomArtistId (random_began_at);
                    if (random_artist_id == null && repeat) {
                        random_began_at = last_random;
                        random_artist_id = GetRandomArtistId (random_began_at);
                    }
                }

                if (random_artist_id != null) {
                    return cache.GetSingle (random_by_artist_fragment, (int)random_artist_id, random_began_at, random_began_at);
                }
                return null;
            } else if (mode == PlaybackShuffleMode.Rating) {
                int random_rating = GetRandomRating (random_began_at);
                if (random_rating == 0 && repeat) {
                    random_began_at = last_random;
                    random_rating = GetRandomRating (random_began_at);
                }

                if (random_rating != 0) {
                    return cache.GetSingle (random_by_rating_fragment, random_began_at, random_began_at, random_rating, random_rating);
                }
                return null;
            } else {
                random_album_id = random_artist_id = null;
            }

            return cache.GetSingle (random_fragment, random_began_at, random_began_at);
        }

        private int? GetRandomAlbumId (DateTime stamp)
        {
            // Get a new Album that hasn't been played since y
            int? album_id = null;
            var reader = connection.Query (@"
                    SELECT a.AlbumID, a.Title, MAX(t.LastPlayedStamp) as LastPlayed, MAX(t.LastSkippedStamp) as LastSkipped
                    FROM CoreTracks t, CoreAlbums a, CoreCache c
                    WHERE
                        c.ModelID = ? AND
                        t.TrackID = c.ItemID AND
                        t.AlbumID = a.AlbumID AND
                        t.LastStreamError = 0
                    GROUP BY t.AlbumID
                    HAVING
                        (LastPlayed < ? OR LastPlayed IS NULL) AND
                        (LastSkipped < ? OR LastSkipped IS NULL)
                    ORDER BY RANDOM()
                    LIMIT 1",
                CacheId, stamp, stamp
            );

            if (reader.Read ()) {
                album_id = Convert.ToInt32 (reader[0]);
            }

            reader.Dispose ();
            return album_id;
        }

        private int? GetRandomArtistId (DateTime stamp)
        {
            // Get a new Artist that hasn't been played since y
            int? artist_id = null;
            var reader = connection.Query (@"
                    SELECT a.ArtistID, a.ArtistName, MAX(t.LastPlayedStamp) as LastPlayed, MAX(t.LastSkippedStamp) as LastSkipped
                    FROM CoreTracks t, CoreAlbums a, CoreCache c
                    WHERE
                        c.ModelID = ? AND
                        t.TrackID = c.ItemID AND
                        t.AlbumID = a.AlbumID AND
                        t.LastStreamError = 0
                    GROUP BY a.ArtistID
                    HAVING
                        (LastPlayed < ? OR LastPlayed IS NULL) AND
                        (LastSkipped < ? OR LastSkipped IS NULL)
                    ORDER BY RANDOM()
                    LIMIT 1",
                CacheId, stamp, stamp
            );

            if (reader.Read ()) {
                artist_id = Convert.ToInt32 (reader[0]);
            }

            reader.Dispose ();
            return artist_id;
        }

        private readonly Random random = new Random ();
        private int GetRandomRating (DateTime stamp)
        {
            // Default rating for unrated songs.
            const int unrated_rating = 3;
            // counts[x] = number of tracks rated x + 1.
            int[] counts = new int[5];
            // Get the distribution of ratings for tracks that haven't been played since stamp.
            using (var reader = connection.Query (@"
                SELECT t.Rating, COUNT(*)
                FROM CoreTracks AS t
                JOIN CoreCache AS c ON c.ItemID = t.TrackID
                WHERE
                    c.ModelID = ? AND
                    t.LastStreamError = 0 AND
                    (t.LastPlayedStamp < ? OR t.LastPlayedStamp IS NULL) AND
                    (t.LastSkippedStamp < ? OR t.LastSkippedStamp IS NULL)
                GROUP BY t.Rating",
                CacheId, stamp, stamp)) {

                while (reader.Read ()) {
                    int rating = Convert.ToInt32 (reader[0]);
                    int count = Convert.ToInt32 (reader[1]);

                    if (rating < 1 || rating > 5) {
                        rating = unrated_rating;
                    }

                    counts[rating - 1] += count;
                }
            }

            if (counts.Sum () == 0) {
                return 0;
            }

            // We will use powers of phi as weights. Such weights result in songs rated R
            // played as often as songs rated R-1 and R-2 combined.
            const double phi = 1.618033989;

            // If you change the weights make sure ALL of them are strictly positive.
            var weights = Enumerable.Range (0, 5).Select (i => Math.Pow (phi, i)).ToArray ();

            // Apply weights to the counts.
            var weighted_counts = counts.Select ((c, i) => c * weights[i]);

            // Normalise the counts.
            var weighted_total = weighted_counts.Sum ();
            weighted_counts = weighted_counts.Select (c => c / weighted_total);

            // Now that we have our counts, get a weighted random rating.
            double random_value = random.NextDouble ();
            int current_rating = 0;
            foreach (var weighted_count in weighted_counts) {
                current_rating++;
                random_value -= weighted_count;
                if (random_value <= 0.0) {
                    break;
                }
            }
            return current_rating;
        }

#endregion

        public override TrackInfo this[int index] {
            get {
                lock (this) {
                    return cache.GetValue (index);
                }
            }
        }

        public override int Count {
            get { return (int) filtered_count; }
        }

        public TimeSpan Duration {
            get { return filtered_duration; }
        }

        public long FileSize {
            get { return filtered_filesize; }
        }
        
        public long UnfilteredFileSize {
            get { return filesize; }
        }
        
        public int UnfilteredCount {
            get { return (int) count; }
            set { count = value; }
        }

        public string UserQuery {
            get { return user_query; }
            set { 
                lock (this) {
                    user_query = value; 
                    have_new_user_query = true;
                }
            }
        }
        
        public string ForcedSortQuery {
            get { return forced_sort_query ? sort_query : null; }
            set { 
                forced_sort_query = value != null;
                sort_query = value;
                if (cache != null) {
                    cache.Clear ();
                }
            }
        }

        public string JoinTable {
            get { return join_table; }
            set {
                join_table = value;
                join_fragment = String.Format (", {0}", join_table);
            }
        }

        public string JoinFragment {
            get { return join_fragment; }
        }

        public string JoinPrimaryKey {
            get { return join_primary_key; }
            set { join_primary_key = value; }
        }

        public string JoinColumn {
            get { return join_column; }
            set { join_column = value; }
        }

        public void AddCondition (string part)
        {
            AddCondition (null, part);
        }
        
        public void AddCondition (string tables, string part)
        {
            if (!String.IsNullOrEmpty (part)) {
                condition = condition == null ? part : String.Format ("{0} AND {1}", condition, part);

                if (!String.IsNullOrEmpty (tables)) {
                    condition_from = condition_from == null ? tables : String.Format ("{0}, {1}", condition_from, tables);
                }
            }
        }
        
        public string Condition {
            get { return condition; }
        }

        private string condition_from_fragment;
        public string ConditionFromFragment {
            get {
                if (condition_from_fragment == null) {
                    if (JoinFragment == null) {
                        condition_from_fragment = condition_from;
                    } else {
                        if (condition_from == null) {
                            condition_from = "CoreTracks";
                        }

                        condition_from_fragment = String.Format ("{0}{1}", condition_from, JoinFragment);
                    }
                }

                return condition_from_fragment;
            }
        }

        public string ConditionFragment {
            get { return PrefixCondition ("AND"); }
        }

        private string PrefixCondition (string prefix)
        {
            string condition = Condition;
            return String.IsNullOrEmpty (condition)
                ? String.Empty
                : String.Format (" {0} {1} ", prefix, condition);
        }

        public int CacheId {
            get { return (int) cache.CacheId; }
        }

        public ISortableColumn SortColumn { 
            get { return sort_column; }
        }
                
        public virtual int RowsInView {
            protected get { return rows_in_view; }
            set { rows_in_view = value; }
        }

        int IExportableModel.GetLength () 
        {
            return Count;
        }
        
        IDictionary<string, object> IExportableModel.GetMetadata (int index)
        {
            return this[index].GenerateExportable ();
        }

        private string track_ids_sql;
        public string TrackIdsSql {
            get {
                if (track_ids_sql == null) {
                    if (!CachesJoinTableEntries) {
                        track_ids_sql = "ItemID FROM CoreCache WHERE ModelID = ? LIMIT ?, ?";
                    } else {
                        track_ids_sql = String.Format (
                            "{0} FROM {1} WHERE {2} IN (SELECT ItemID FROM CoreCache WHERE ModelID = ? LIMIT ?, ?)",
                            JoinColumn, JoinTable, JoinPrimaryKey
                        );
                    }
                }
                return track_ids_sql;
            }
        }

        private bool caches_join_table_entries = false;
        public bool CachesJoinTableEntries {
            get { return caches_join_table_entries; }
            set { caches_join_table_entries = value; }
        }

        // Implement ICacheableModel
        public int FetchCount {
            get { return RowsInView > 0 ? RowsInView * 5 : 100; }
        }

        public string SelectAggregates {
            get { return "SUM(CoreTracks.Duration), SUM(CoreTracks.FileSize)"; }
        }

        // Implement IDatabaseModel
        public string ReloadFragment {
            get { return reload_fragment; }
        }
        
        public bool CachesValues { get { return false; } }
    }
}
