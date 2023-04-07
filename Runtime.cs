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
			Directory.SetCurrentDirectory(gamedir);
			WindowsLaunch.Main(new[] { "-steam", "-lobby", "friends", "-config", "serverconfig.txt" });
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
			var buf = buffer.readBuffer;
			switch (buf[start])
            {
				// Spawn
				case 12:
					Player player = Main.player[buf[start + 1]];
					if (PlayerSpawnContext.RecallFromItem == (PlayerSpawnContext)buf[start + 14])
					{
						if (player.statLife != player.statLifeMax)
						{
							var alert = NetworkText.FromFormattable(
								"{0} recalled after taking {1} hearts of damage",
								player.name,
								(float)(player.statLifeMax - player.statLife) / 20
							);
							Terraria.Chat.ChatHelper.BroadcastChatMessage(alert, Color.Red);
						}

					}
					break;
				default: break;
			}
			next(buffer, start, length, out messageType);
		}
	}
}
