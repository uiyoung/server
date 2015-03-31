﻿/*
 * This file is part of Project Hybrasyl.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the Affero General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful, but
 * without ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE. See the Affero General Public License
 * for more details.
 *
 * You should have received a copy of the Affero General Public License along
 * with this program. If not, see <http://www.gnu.org/licenses/>.
 *
 * (C) 2013 Justin Baugh (baughj@hybrasyl.com)
 * (C) 2015 Project Hybrasyl (info@hybrasyl.com)
 *
 * Authors:   Justin Baugh  <baughj@hybrasyl.com>
 *            Kyle Speck    <kojasou@hybrasyl.com>
 */

using C3;
using Hybrasyl.Objects;
using Hybrasyl.Properties;
using Hybrasyl.Utility;
using log4net;
using SharpYaml;
using SharpYaml.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace Hybrasyl.Properties
{
    public partial class Door
    {
        public bool Open { get; set; }
    }
}

namespace Hybrasyl
{


    public class MapPoint
    {
        public static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Int64 Id { get; set; }
        public string Pointname { get; set; }
        public WorldMap Parent { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Name { get; set; }
        public ushort DestinationMap { get; set; }
        public byte DestinationX { get; set; }
        public byte DestinationY { get; set; }

        public int XOffset { get; set; }
        public int YOffset { get; set; }
        public int XQuadrant { get; set; }
        public int YQuadrant { get; set; }

        public MapPoint()
        {
            return;
        }

        public byte[] GetBytes()
        {
            var buffer = Encoding.GetEncoding(949).GetBytes(Name);
            Logger.DebugFormat("buffer is {0} and Name is {1}", BitConverter.ToString(buffer), Name);

            // X quadrant, offset, Y quadrant, offset, length of the name, the name, plus a 64-bit(?!) ID
            List<Byte> bytes = new List<Byte>();

            Logger.DebugFormat("{0}, {1}, {2}, {3}, {4}, mappoint ID is {5}", XQuadrant, XOffset, YQuadrant,
                YOffset, Name.Length, Id);

            bytes.Add((byte)XQuadrant);
            bytes.Add((byte)XOffset);
            bytes.Add((byte)YQuadrant);
            bytes.Add((byte)YOffset);
            bytes.Add((byte)Name.Length);
            bytes.AddRange(buffer);
            bytes.AddRange(BitConverter.GetBytes(Id));

            return bytes.ToArray();

        }


    }

    public class WorldMap
    {
        public static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public int Id { get; set; }
        public string Name { get; set; }
        public string ClientMap { get; set; }
        public List<MapPoint> Points { get; set; }
        public World World { get; set; }

        public WorldMap()
        {
            Points = new List<MapPoint>();
        }

        public byte[] GetBytes()
        {
            // Returns the representation of the worldmap as an array of bytes, 
            // suitable to passing to a map packet.

            var buffer = Encoding.GetEncoding(949).GetBytes(ClientMap);
            List<Byte> bytes = new List<Byte>();

            bytes.Add((byte)ClientMap.Length);
            bytes.AddRange(buffer);
            bytes.Add((byte)Points.Count);
            bytes.Add(0x00);

            foreach (var mappoint in Points)
            {
                bytes.AddRange(mappoint.GetBytes());
            }

            Logger.DebugFormat("I am sending the following map packet:");
            Logger.DebugFormat("{0}", BitConverter.ToString(bytes.ToArray()));

            return bytes.ToArray();
        }
    }

    public class Map
    {
        public static readonly ILog Logger =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public ushort Id { get; set; }
        public String Size { get; set; }    
        public byte X { get; set; }
        public byte Y { get; set; }
        public string Name { get; set; }
        public byte Flags { get; set; }
        public byte Music { get; set; }
        public World World { get; set; }
        public byte[] RawData { get; set; }
        public ushort Checksum { get; set; }
        public bool[,] IsWall { get; set; }

        public List<Warp> Warps { get; set; }
        public string Message { get; set; }

        public QuadTree<VisibleObject> EntityTree { get; set; }

        public HashSet<VisibleObject> Objects { get; private set; }
        public Dictionary<string, User> Users { get; private set; }

        public Dictionary<Tuple<byte, byte>, WorldWarp> WorldWarps { get; set; }
        public Dictionary<Tuple<byte, byte>, Objects.Door> Doors { get; set; }
        public Dictionary<Tuple<byte, byte>, Signpost> Signposts { get; set; }
        public Dictionary<Tuple<byte, byte>, Reactor> Reactors { get; set; }

        private void Initialize()
        {
            RawData = new byte[0];
            Objects = new HashSet<VisibleObject>();
            Users = new Dictionary<string, User>();
            Warps = new List<Warp>();
            WorldWarps = new Dictionary<Tuple<byte, byte>, WorldWarp>();
            Doors = new Dictionary<Tuple<byte, byte>, Objects.Door>();
            Signposts = new Dictionary<Tuple<byte, byte>, Signpost>();
            Reactors = new Dictionary<Tuple<byte, byte>, Reactor>();
        }

        public Map()
        {
            Initialize();
        }

        public Map(YamlStream stream, String filename)
        {
            Initialize();
            // You may be wondering why we do it this way, vs just serializing or deserializing a 
            // document. This method, although more wordy / lengthy, turns out to be about 7x faster
            // than deserializing.
            var mapping = (YamlMappingNode) stream.Documents[0].RootNode;
            var mapdata = YamlHelper.GetDictionary(mapping.Children);

            Id = YamlHelper.GetRequiredValue(mapdata, "id", typeof(ushort));
            Size = YamlHelper.GetRequiredValue(mapdata, "size", typeof(String));
            Name = YamlHelper.GetRequiredValue(mapdata, "name", typeof(String));
            Music = YamlHelper.GetOptionalValue(mapdata, "music", typeof(byte), byte.MinValue);

            dynamic warps;
            if (mapdata.TryGetValue("warps", out warps))
            {
                if (warps is List<dynamic>)
                {
                    var numWarps = 0;
                    foreach (var warp in warps as List<dynamic>)
                    {
                        numWarps++;
                        var errorMsg = String.Format("{0}: warp #{1}: {{2}} is required", filename, numWarps);

                        var x = YamlHelper.GetRequiredValue(warp, "x", typeof(byte), errorMsg);
                        var y = YamlHelper.GetRequiredValue(warp, "y", typeof(byte), errorMsg);
                        var targetX = YamlHelper.GetRequiredValue(warp, "target_x", typeof(byte), errorMsg);
                        var targetY = YamlHelper.GetRequiredValue(warp, "target_y", typeof(byte), errorMsg);
                        var targetMap = YamlHelper.GetRequiredValue(warp, "target_map", typeof(ushort), errorMsg);
                        var minimumAbility = YamlHelper.GetOptionalValue(warp, "min_ab", typeof (byte), byte.MinValue);
                        var minimumLevel = YamlHelper.GetOptionalValue(warp, "min_lev", typeof(byte), byte.MinValue);
                        var maximumLevel = YamlHelper.GetOptionalValue(warp, "max_lev", typeof(byte), byte.MaxValue);
                        var mobsCanUse = YamlHelper.GetOptionalValue(warp, "mob_use", typeof (bool), false);
                        Warps.Add(new Warp(x, y, targetMap, targetX, targetY, minimumLevel, maximumLevel, minimumAbility,
                            mobsCanUse));
                    }

                }
                else
                    throw new YamlException(String.Format("{0}: warps must be a list of warps", filename));

            }



        }

        public List<VisibleObject> GetTileContents(int x, int y)
        {
            return EntityTree.GetObjects(new Rectangle(x, y, 1, 1));
        }

        public void InsertNpc(npc toinsert)
        {
            var merchant = new Merchant(toinsert);
            World.Insert(merchant);
            Insert(merchant, merchant.X, merchant.Y);
            merchant.OnSpawn();
        }
        
        public void InsertReactor(reactor toinsert)
        {
            var reactor = new Reactor(toinsert);
            World.Insert(reactor);
            Insert(reactor, reactor.X, reactor.Y);
            reactor.OnSpawn();
        }

        public void InsertSignpost(signpost toinsert)
        {
            Logger.InfoFormat("Inserting signpost {0}@{1},{2}", toinsert.map.name, toinsert.map_x, toinsert.map_y);
            var post = new Signpost(toinsert);
            World.Insert(post);
            Insert(post, post.X, post.Y);
            Signposts[new Tuple<byte, byte>(post.X, post.Y)] = post;
        }

        private void InsertDoor(byte x, byte y, bool open, bool isLeftRight, bool triggerCollision = true)
        {
            var door = new Objects.Door(x, y, open, isLeftRight, triggerCollision);
            World.Insert(door);
            Insert(door, door.X, door.Y);
            Doors[new Tuple<byte, byte>(door.X, door.Y)] = door;
        }

        public bool Load()
        {
            IsWall = new bool[X, Y];
            var filename = Path.Combine(Constants.DataDirectory, string.Format("world\\mapfiles\\lod{0}.map", Id));

            if (File.Exists(filename))
            {
                RawData = File.ReadAllBytes(filename);
                Checksum = Crc16.Calculate(RawData);

                int index = 0;
                for (int y = 0; y < Y; ++y)
                {
                    for (int x = 0; x < X; ++x)
                    {
                        var bg = RawData[index++] | RawData[index++] << 8;
                        var lfg = RawData[index++] | RawData[index++] << 8;
                        var rfg = RawData[index++] | RawData[index++] << 8;

                        if (lfg != 0 && (Game.Collisions[lfg - 1] & 0x0F) == 0x0F)
                        {
                            IsWall[x, y] = true;
                        }

                        if (rfg != 0 && (Game.Collisions[rfg - 1] & 0x0F) == 0x0F)
                        {
                            IsWall[x, y] = true;
                        }

                        ushort lfgu = (ushort)lfg;
                        ushort rfgu = (ushort)rfg;

                        if (Game.DoorSprites.ContainsKey(lfgu))
                        {
                            // This is a left-right door
                            Logger.DebugFormat("Inserting LR door at {0}@{1},{2}: Collision: {3}",
                                Name, x, y, IsWall[x, y]);

                            InsertDoor((byte)x, (byte)y, IsWall[x, y], true,
                            Game.IsDoorCollision(lfgu));
                        }
                        else if (Game.DoorSprites.ContainsKey(rfgu))
                        {
                            Logger.DebugFormat("Inserting UD door at {0}@{1},{2}: Collision: {3}",
                                Name, x, y, IsWall[x, y]);
                            // THis is an up-down door 
                            InsertDoor((byte)x, (byte)y, IsWall[x, y], false,
                                Game.IsDoorCollision(rfgu));
                        }

                    }
                }

                return true;
            }

            return false;
        }


        public void Insert(VisibleObject obj, byte x, byte y, bool updateClient = true)
        {
            if (Objects.Add(obj))
            {
                obj.Map = this;
                obj.X = x;
                obj.Y = y;

                EntityTree.Add(obj);

                var user = obj as User;
                if (user != null)
                {
                    if (updateClient)
                    {
                        obj.SendMapInfo();
                        obj.SendLocation();
                    }
                    Users.Add(user.Name, user);
                }

                var value = obj as Reactor;
                if (value != null)
                {
                    Reactors.Add(new Tuple<byte, byte>((byte)x,(byte)y), value);
                }

                var affectedObjects = EntityTree.GetObjects(obj.GetViewport());

                foreach (var target in affectedObjects)
                {
                    target.AoiEntry(obj);
                    obj.AoiEntry(target);
                }

            }
        }


        /// <summary>
        /// Toggle a given door's state (open/closed) and send updates to users nearby.
        /// </summary>
        /// <param name="x">The X coordinate of the door.</param>
        /// <param name="y">The Y coordinate of the door.</param>
        /// <returns></returns>
        public void ToggleDoor(byte x, byte y)
        {
            var coords = new Tuple<byte, byte>(x, y);
            Logger.DebugFormat("Door {0}@{1},{2}: Open: {3}, changing to {4}",
                Name, x, y, Doors[coords].Closed,
                !Doors[coords].Closed);

            Doors[coords].Closed = !Doors[coords].Closed;

            // There are several examples of doors in Temuair that trigger graphic
            // changes but do not trigger collision updates (e.g. 3-panel doors in
            // Piet & Undine).
            if (Doors[coords].UpdateCollision)
            {
                Logger.DebugFormat("Door {0}@{1},{2}: updateCollision is set, collisions are now {3}",
                    Name, x, y, !Doors[coords].Closed);
                IsWall[x, y] = !IsWall[x, y];
            }

            Logger.DebugFormat("Toggling door at {0},{1}", x, y);
            Logger.DebugFormat("Door is now in state: Open: {0} Collision: {1}", Doors[coords].Closed, IsWall[x, y]);

            var updateViewport = GetViewport(x, y);

            foreach (var obj in EntityTree.GetObjects(updateViewport))
            {
                if (obj is User)
                {
                    var user = obj as User;
                    Logger.DebugFormat("Sending door packet to {0}: X {1}, Y {2}, Open {3}, LR {4}",
                        user.Name, x, y, Doors[coords].Closed,
                        Doors[coords].IsLeftRight);

                    user.SendDoorUpdate(x, y, Doors[coords].Closed,
                        Doors[coords].IsLeftRight);
                }
            }
        }

        /// <summary>
        /// Toggles a door panel at x,y and depending on whether there are doors
        /// next to it, will open those as well. This code is pretty ugly, to boot.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void ToggleDoors(byte x, byte y)
        {
            var coords = new Tuple<byte, byte>(x, y);
            var door = Doors[coords];

            // First, toggle the actual door itself

            ToggleDoor(x, y);

            // Now, toggle any potentially adjacent "doors"

            if (door.IsLeftRight)
            {
                // Look for a door at x-1, x+1, and open if they're present
                Objects.Door nextdoor;
                var door1Coords = new Tuple<byte, byte>((byte)(x - 1), (byte)(y));
                var door2Coords = new Tuple<byte, byte>((byte)(x + 1), (byte)(y));
                if (Doors.TryGetValue(door1Coords, out nextdoor))
                {
                    ToggleDoor((byte)(x - 1), (byte)(y));
                }
                if (Doors.TryGetValue(door2Coords, out nextdoor))
                {
                    ToggleDoor((byte)(x + 1), (byte)(y));
                }

            }
            else
            {
                // Look for a door at y-1, y+1 and open if they're present
                Objects.Door nextdoor;
                var door1Coords = new Tuple<byte, byte>((byte)(x), (byte)(y - 1));
                var door2Coords = new Tuple<byte, byte>((byte)(x), (byte)(y + 1));
                if (Doors.TryGetValue(door1Coords, out nextdoor))
                {
                    ToggleDoor((byte)(x), (byte)(y - 1));
                }
                if (Doors.TryGetValue(door2Coords, out nextdoor))
                {
                    ToggleDoor((byte)(x), (byte)(y + 1));
                }
            }
        }


        public Rectangle GetViewport(byte x, byte y)
        {
            return new Rectangle((x - Constants.VIEWPORT_SIZE / 2),
                (y - Constants.VIEWPORT_SIZE / 2), Constants.VIEWPORT_SIZE,
                Constants.VIEWPORT_SIZE);
        }

        public Rectangle GetShoutViewport(byte x, byte y)
        {
            return new Rectangle((x - Constants.VIEWPORT_SIZE),
                (y - Constants.VIEWPORT_SIZE), Constants.VIEWPORT_SIZE * 2,
                Constants.VIEWPORT_SIZE * 2);
        }

        public void Remove(VisibleObject obj)
        {
            if (Objects.Remove(obj))
            {
                EntityTree.Remove(obj);

                if (obj is User)
                {
                    var user = obj as User;
                    Users.Remove(user.Name);
                    if (user.ActiveExchange != null)
                        user.ActiveExchange.CancelExchange(user);
                }

                var affectedObjects = EntityTree.GetObjects(obj.GetViewport());

                foreach (var target in affectedObjects)
                {
                    // If the target of a Remove is a player, we insert a 250ms delay to allow the animation
                    // frame to complete.
                    if (target is User)
                        ((User)target).AoiDeparture(obj, 250);
                    else
                        target.AoiDeparture(obj);

                    obj.AoiDeparture(target);
                }

                obj.Map = null;
                obj.X = 0;
                obj.Y = 0;
            }
        }

        public void AddGold(int x, int y, Gold gold)
        {
            Logger.DebugFormat("{0}, {1}, {2} qty {3} id {4}",
                x, y, gold.Name, gold.Amount, gold.Id);
            if (gold == null)
            {
                Logger.DebugFormat("Item is null, aborting");
                return;
            }
            // Add the gold to the world at the given location.
            gold.X = (byte)x;
            gold.Y = (byte)y;
            gold.Map = this;
            EntityTree.Add(gold);
            Objects.Add(gold);
            NotifyNearbyAoiEntry(gold);
        }

        public void AddItem(int x, int y, Item item)
        {
            Logger.DebugFormat("{0}, {1}, {2} qty {3} id {4}",
                x, y, item.Name, item.Count, item.Id);
            if (item == null)
            {
                Logger.DebugFormat("Item is null, aborting");
                return;
            }            
            // Add the item to the world at the given location.
            item.X = (byte)x;
            item.Y = (byte)y;
            item.Map = this;
            EntityTree.Add(item);
            Objects.Add(item);
            NotifyNearbyAoiEntry(item);
        }

        public void RemoveGold(Gold gold)
        {
            // Remove the gold from the world at the specified location.
            Logger.DebugFormat("Removing {0} qty {1} id {2}", gold.Name, gold.Amount, gold.Id);
            NotifyNearbyAoiDeparture(gold);
            EntityTree.Remove(gold);
            Objects.Remove(gold);
        }

        public void RemoveItem(Item item)
        {
            // Remove the item from the world at the specified location.
            Logger.DebugFormat("Removing {0} qty {1} id {2}", item.Name, item.Count, item.Id);
            NotifyNearbyAoiDeparture(item);
            EntityTree.Remove(item);
            Objects.Remove(item);
        }


        public void NotifyNearbyAoiEntry(VisibleObject objectToAdd)
        {
            foreach (var obj in EntityTree.GetObjects(objectToAdd.GetViewport()))
            {
                if (obj is User)
                {
                    Logger.DebugFormat("Notifying {0} of item {1} at {2},{3} with sprite {4}", obj.Name, objectToAdd.Name,
                        objectToAdd.X, objectToAdd.Y, objectToAdd.Sprite);
                    var user = obj as User;
                    user.AoiEntry(objectToAdd);
                }
            }
        }

        public void NotifyNearbyAoiDeparture(VisibleObject objectToRemove)
        {
            foreach (var obj in EntityTree.GetObjects(objectToRemove.GetViewport()))
            {
                if (obj is User)
                {
                    var user = obj as User;
                    user.AoiDeparture(objectToRemove);
                }
            }

        }

        public bool IsValidPoint(short x, short y)
        {
            return x >= 0 && x < X && y >= 0 && y < Y;
        }




 
    }

    public struct Warp
    {
        public byte X { get; set; }
        public byte Y { get; set; }
        public ushort DestinationMap { get; set; }
        public byte DestinationX { get; set; }
        public byte DestinationY { get; set; }
        public byte MinimumLevel { get; set; }
        public byte MaximumLevel { get; set; }
        public byte MinimumAbility { get; set; }
        public bool MobsCanUse { get; set; }

        public Warp(byte x, byte y, ushort destinationMap, byte destinationX, byte destinationY,
            byte minimumLevel = 1,
            byte maximumLevel = 99, byte minimumAbility = 0, bool mobsCanUse = true) : this()
        {
            X = x;
            Y = y;
            DestinationMap = destinationMap;
            DestinationY = destinationY;
            DestinationX = destinationX;
            MinimumAbility = minimumAbility;
            MinimumLevel = minimumLevel;
            MaximumLevel = maximumLevel;
            MobsCanUse = mobsCanUse;
        }

    }

    public struct WorldWarp
    {
        public byte X { get; set; }
        public byte Y { get; set; }
        public byte WorldmapId { get; set; }
        public byte MinimumLevel { get; set; }
        public byte MaximumLevel { get; set; }
        public byte MinimumAbility { get; set; }
        public WorldMap DestinationWorldMap { get; set; }
    }

    public struct Point
    {
        public static int Distance(int x1, int y1, int x2, int y2)
        {
            return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
        }
        public static int Distance(VisibleObject obj1, VisibleObject obj2)
        {
            return Distance(obj1.X, obj1.Y, obj2.X, obj2.Y);
        }
        public static int Distance(VisibleObject obj, int x, int y)
        {
            return Distance(obj.X, obj.Y, x, y);
        }
    }
}