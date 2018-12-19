// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.IO.Network;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Rulesets;
using osu.Game.Screens.Multi.Lounge.Components;

namespace osu.Game.Screens.Multi
{
    public class RoomManager : PollingComponent
    {
        public IBindableCollection<Room> Rooms => rooms;
        private readonly BindableCollection<Room> rooms = new BindableCollection<Room>();

        public readonly Bindable<Room> Current = new Bindable<Room>();

        private FilterCriteria currentFilter = new FilterCriteria();

        [Resolved]
        private APIAccess api { get; set; }

        [Resolved]
        private RulesetStore rulesets { get; set; }

        [Resolved]
        private BeatmapManager beatmaps { get; set; }

        public RoomManager()
        {
            TimeBetweenPolls = 5000;
        }

        public void CreateRoom(Room room)
        {
            room.Host.Value = api.LocalUser;

            var req = new CreateRoomRequest(room);

            req.Success += result => addRoom(room, result);
            req.Failure += exception => Logger.Log($"Failed to create room: {exception}");
            api.Queue(req);
        }

        public void Filter(FilterCriteria criteria)
        {
            currentFilter = criteria;
            PollImmediately();
        }

        protected override Task Poll()
        {
            if (!api.IsLoggedIn)
                return base.Poll();

            var tcs = new TaskCompletionSource<bool>();

            var pollReq = new GetRoomsRequest(currentFilter.PrimaryFilter);

            pollReq.Success += result =>
            {
                // Remove past matches
                foreach (var r in rooms.ToList())
                {
                    if (result.All(e => e.RoomID.Value != r.RoomID.Value))
                        rooms.Remove(r);
                }

                // Add new matches, or update existing
                foreach (var r in result)
                {
                    processPlaylist(r);

                    var existing = rooms.FirstOrDefault(e => e.RoomID.Value == r.RoomID.Value);
                    if (existing == null)
                        rooms.Add(r);
                    else
                        existing.CopyFrom(r);
                }

                tcs.SetResult(true);
            };

            pollReq.Failure += _ => tcs.SetResult(false);

            api.Queue(pollReq);

            return tcs.Task;
        }

        private void addRoom(Room local, Room remote)
        {
            processPlaylist(remote);

            local.CopyFrom(remote);

            var existing = rooms.FirstOrDefault(e => e.RoomID.Value == local.RoomID.Value);
            if (existing != null)
                rooms.Remove(existing);
            rooms.Add(local);
        }

        private void processPlaylist(Room room)
        {
            foreach (var pi in room.Playlist)
                pi.MapObjects(beatmaps, rulesets);
        }

        private class CreateRoomRequest : APIRequest<Room>
        {
            private readonly Room room;

            public CreateRoomRequest(Room room)
            {
                this.room = room;
            }

            protected override WebRequest CreateWebRequest()
            {
                var req = base.CreateWebRequest();

                req.ContentType = "application/json";
                req.Method = HttpMethod.Post;

                req.AddRaw(JsonConvert.SerializeObject(room));

                return req;
            }

            protected override string Target => "rooms";
        }

        private class GetRoomsRequest : APIRequest<List<Room>>
        {
            private readonly PrimaryFilter primaryFilter;

            public GetRoomsRequest(PrimaryFilter primaryFilter)
            {
                this.primaryFilter = primaryFilter;
            }

            protected override WebRequest CreateWebRequest()
            {
                var req = base.CreateWebRequest();

                if (primaryFilter == PrimaryFilter.Participated)
                    req.AddParameter("participated", "1");

                return req;
            }

            protected override string Target => "rooms";
        }
    }
}
