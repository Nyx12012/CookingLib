namespace CookingLib
{
    internal static class PlayerIds
    {
        internal static Player Of(ConsumptionManager consumer)
        {
            if (consumer == null)
                return null;
            PlayerController controller = consumer.Player;
            Player player = controller == null ? null : controller.NetworkPlayer;
            return player == null ? null : player;
        }

        internal static Player Find(string name)
        {
            string key = (name ?? "").Trim();
            foreach (object candidate in Player.AllPlayers)
            {
                Player player = candidate as Player;
                if (player != null && player.UserInfo != null && string.Equals(player.UserInfo.Username, key, System.StringComparison.OrdinalIgnoreCase))
                    return player;
            }
            return null;
        }

        internal static UserInfoAndRole InfoOf(Player player) => player == null ? null : player.ConnectionToRemotePlayer?.UserInfo;
    }
}
