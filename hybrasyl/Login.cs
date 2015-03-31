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

using Hybrasyl.Enums;
using Hybrasyl.Properties;
using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hybrasyl
{

    public class Login : Server
    {
        public new LoginPacketHandler[] PacketHandlers { get; private set; }

        public Login(int port)
            : base(port)
        {
            Logger.InfoFormat("LoginConstructor: port is {0}", port);

            PacketHandlers = new LoginPacketHandler[256];

            for (int i = 0; i < 256; ++i)
                PacketHandlers[i] = (c, p) => Logger.WarnFormat("Login: Unhandled opcode 0x{0:X2}", p.Opcode);

            PacketHandlers[0x02] = PacketHandler_0x02_CreateA;
            PacketHandlers[0x03] = PacketHandler_0x03_Login;
            PacketHandlers[0x04] = PacketHandler_0x04_CreateB;
            PacketHandlers[0x10] = PacketHandler_0x10_ClientJoin;
            PacketHandlers[0x4B] = PacketHandler_0x4B_RequestNotification;
            PacketHandlers[0x68] = PacketHandler_0x68_RequestHomepage;
        }

        public World World { get; private set; }

        private void PacketHandler_0x02_CreateA(Client client, ClientPacket packet)
        {
            var name = packet.ReadString8();
            var password = packet.ReadString8();
            var email = packet.ReadString8();

            if (Game.World.PlayerExists(name))
            {
                client.LoginMessage("That name is unavailable.", 3);
            }
            else if (name.Length < 4 || name.Length > 12)
            {
                client.LoginMessage("Names must be between 4 to 12 characters long.", 3);
            }
            else if (password.Length < 4 || password.Length > 8)
            {
                client.LoginMessage("Passwords must be between 4 and 8 characters long.", 3);
            }
            else if (name == password)
            {
                client.LoginMessage("Your password may not be the same as your username.", 3);
            }
            else if (Regex.IsMatch(name, "^[A-Za-z]{4,12}$"))
            {
                client.NewCharacterName = name;
                client.NewCharacterPassword = BCrypt.Net.BCrypt.HashPassword(password,
                    BCrypt.Net.BCrypt.GenerateSalt(12));
                client.LoginMessage("\0", 0);
            }
            else
            {
                client.LoginMessage("Names may only contain letters.", 3);
            }
        }


        private void PacketHandler_0x03_Login(Client client, ClientPacket packet)
        {
            var name = packet.ReadString8();
            var password = packet.ReadString8();
            Logger.DebugFormat("cid {0}: Login request for {1}", client.ConnectionId, name);

            using (var ctx = new hybrasylEntities(Constants.ConnectionString))
            {

                var result = ctx.players.Where(player => player.name == name).SingleOrDefault();

                if (result == null)
                {
                    client.LoginMessage("That character does not exist", 3);
                    Logger.InfoFormat("cid {0}: attempt to login as nonexistent character {1}", client.ConnectionId, name);
                }
                else
                {
                    if (BCrypt.Net.BCrypt.Verify(password, result.password_hash))
                    {
                        Logger.DebugFormat("cid {0}: password verified for {1}", client.ConnectionId, name);

                        if (Game.World.ActiveUsersByName.ContainsKey(name))
                        {
                            Logger.InfoFormat("cid {0}: {1} logging on again, disconnecting previous connection",
                                client.ConnectionId, name);
                            World.MessageQueue.Add(new HybrasylControlMessage(ControlOpcodes.LogoffUser, name));
                        }

                        Logger.DebugFormat("cid {0} ({1}): logging in", client.ConnectionId, name);
                        client.LoginMessage("\0", 0);
                        client.SendMessage("Welcome to Hybrasyl!", 3);
                        Logger.DebugFormat("cid {0} ({1}): sending redirect to world", client.ConnectionId, name);

                        var redirect = new Redirect(client, this, Game.World, name, client.EncryptionSeed,
                            client.EncryptionKey);
                        Logger.InfoFormat("cid {0} ({1}): login successful, redirecting to world server",
                            client.ConnectionId, name);
                        client.Redirect(redirect);
                    }
                    else
                    {
                        Logger.WarnFormat("cid {0} ({1}): password incorrect", client.ConnectionId, name);
                        client.LoginMessage("Incorrect password", 3);
                    }
                }
            }
        }



        private void PacketHandler_0x04_CreateB(Client client, ClientPacket packet)
        {
            if (string.IsNullOrEmpty(client.NewCharacterName) || string.IsNullOrEmpty(client.NewCharacterPassword))
                return;

            var hairStyle = packet.ReadByte();
            var sex = packet.ReadByte();
            var hairColor = packet.ReadByte();

            if (hairStyle < 1)
                hairStyle = 1;

            if (hairStyle > 17)
                hairStyle = 17;

            if (hairColor > 13)
                hairColor = 13;

            if (sex < 1)
                sex = 1;

            if (sex > 2)
                sex = 2;

            if (!Game.World.PlayerExists(client.NewCharacterName))
            {
                using (var ctx = new hybrasylEntities(Constants.ConnectionString))
                {
                    player newplayer = new player
                    {
                        name = client.NewCharacterName,
                        password_hash = client.NewCharacterPassword,
                        sex = (Sex) sex,
                        hairstyle = hairStyle,
                        haircolor = hairColor,
                        map_id = 136,
                        map_x = 10,
                        map_y = 10,
                        direction = 1,
                        class_type = 0,
                        level = 1,
                        exp = 0,
                        ab = 0,
                        gold = 0,
                        ab_exp = 0,
                        max_hp = 50,
                        max_mp = 50,
                        cur_hp = 50,
                        cur_mp = 35,
                        str = 3,
                        @int = 3,
                        wis = 3,
                        con = 3,
                        dex = 3,
                        inventory = "[]",
                        equipment = "[]",
                        created_at = DateTime.Now
                    };
                    try
                    {
                        ctx.players.Add(newplayer);
                        ctx.SaveChanges();
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorFormat("Error saving new player!");
                        Logger.ErrorFormat(e.ToString());
                        client.LoginMessage("Unknown error. Contact admin@hybrasyl.com", 3);
                    }
                    client.LoginMessage("\0", 0);
                }
            }
 
        }
        private void PacketHandler_0x10_ClientJoin(Client client, ClientPacket packet)
        {
            var seed = packet.ReadByte();
            var keyLength = packet.ReadByte();
            var key = packet.Read(keyLength);
            var name = packet.ReadString8();
            var id = packet.ReadUInt32();

            var redirect = ExpectedConnections[id];
            if (redirect.Matches(name, key, seed))
            {
                ((IDictionary)ExpectedConnections).Remove(id);

                client.EncryptionKey = key;
                client.EncryptionSeed = seed;

                if (redirect.Source is Lobby)
                {
                    var x60 = new ServerPacket(0x60);
                    x60.WriteByte(0x00);
                    x60.WriteUInt32(Game.NotificationCrc);
                    client.Enqueue(x60);
                }
            }

        }
        private void PacketHandler_0x4B_RequestNotification(Client client, ClientPacket packet)
        {
            var x60 = new ServerPacket(0x60);
            x60.WriteByte(0x01);
            x60.WriteUInt16((ushort)Game.Notification.Length);
            x60.Write(Game.Notification);
            client.Enqueue(x60);
        }

        private void PacketHandler_0x68_RequestHomepage(Client client, ClientPacket packet)
        {
            var x03 = new ServerPacket(0x66);
            x03.WriteByte(0x03);
            x03.WriteString8("http://www.hybrasyl.com");
            client.Enqueue(x03);
        }
    }
}