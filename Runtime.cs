using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Terraria;
using Terraria.Localization;
using Microsoft.Xna.Framework;
using System.IO;

namespace TerrariaMultiplayer
{
	class Entrypoint
	{
		public static void Run(string gamedir)
		{
			System.IO.Directory.SetCurrentDirectory(gamedir);
			Terraria.WindowsLaunch.Main(new[] { "-steam", "-lobby", "friends", "-config", "serverconfig.txt" });
		}
	}
    public class Hooks
	{
		public delegate void UpdateTimeRate();
		public static void Main_UpdateTimeRate(UpdateTimeRate next)
		{
			bool playersAreAsleep = Main.CurrentFrameFlags.SleepingPlayersCount == Main.CurrentFrameFlags.ActivePlayersCount
									&& Main.CurrentFrameFlags.SleepingPlayersCount > 0;
			// If players are sleeping at night
			if (playersAreAsleep && !Main.dayTime && Main.time < 32400.0 && !Main.IsFastForwardingTime())
			{
				Main.fastForwardTimeToDawn = true;
				NetMessage.SendData(7);
			}
			next();
		}

		// When players tell us they've teleported without max health, nag them.
		public delegate void GetData(MessageBuffer buffer, int start, int length, out int messageType);
		public static void MessageBuffer_GetData(MessageBuffer buffer, int start, int length, out int messageType, GetData next)
		{

			// GetData will write messageType
			var reader = new BinaryReader(new MemoryStream(buffer.readBuffer));
			reader.BaseStream.Position = start;
			var msg = reader.ReadByte();

			if (msg == 12)
			{
				// Client says "I've just respawned"
				byte whoAmI = reader.ReadByte();
				Player player14 = Main.player[whoAmI];
				player14.SpawnX = reader.ReadInt16();
				player14.SpawnY = reader.ReadInt16();
				player14.respawnTimer = reader.ReadInt32();
				player14.numberOfDeathsPVE = reader.ReadInt16();
				player14.numberOfDeathsPVP = reader.ReadInt16();
				PlayerSpawnContext playerSpawnContext = (PlayerSpawnContext)reader.ReadByte();
				if (playerSpawnContext == PlayerSpawnContext.RecallFromItem)
				{
					if (player14.statLife != player14.statLifeMax)
					{
						Terraria.Chat.ChatHelper.BroadcastChatMessage(NetworkText.FromFormattable("{0} recalled with {1}hp out of {2}", new[] {
							player14.name,
							player14.statLife.ToString(),
							player14.statLifeMax.ToString()
						}), Color.Red);
					}

				}
			}
			next(buffer, start, length, out messageType);
		}
	}
}
