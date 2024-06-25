using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections;
using Rocket.Core.Commands;
using UnityEngine;
using UnityEngine.Networking;

namespace TeamGamesUnturned
{
    public class TeamGames : RocketPlugin<TeamGamesConfiguration>
    {
        private const string ApiUrl = "https://api.teamgames.io/api/v3/store/transaction/update";

        protected override void Load()
        {
            base.Load();
            Rocket.Core.Logging.Logger.Log("TeamGames plugin has been loaded.");
        }

        protected override void Unload()
        {
            base.Unload();
        }

        [RocketCommand("claim", "Claim your purchases")]
        public void ClaimCommand(IRocketPlayer caller)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            var postData = new Dictionary<string, string> { ["playerName"] = player.Id };
            string jsonData = JsonConvert.SerializeObject(postData);

            Rocket.Core.Logging.Logger.Log($"Sending request to API. URL: {ApiUrl}, Payload: {jsonData}");

            SendMessage(player.Player, "Processing your claim...");

            StartCoroutine(SendPostRequest(ApiUrl, jsonData, Configuration.Instance.StoreSecretKey, player));
        }

        [RocketCommand("teamgames.secret", "Set the TeamGames secret key")]
        public void SetSecretCommand(IRocketPlayer caller, string[] command)
        {
            // if (!(caller is ConsolePlayer) && !caller.IsAdmin)
            // {
                // SendMessage(((UnturnedPlayer)caller).Player, "Command Reserved for Administrators");
                // return;
            // }
            if (command.Length != 1)
            {
                if (caller is UnturnedPlayer uPlayer)
                {
                    SendMessage(uPlayer.Player, "Usage: /teamgames.secret <secret>");
                }
                else
                {
                    Rocket.Core.Logging.Logger.Log("Usage: /teamgames.secret <secret>");
                }
                return;
            }

            Configuration.Instance.StoreSecretKey = command[0];
            Configuration.Save();
            if (caller is UnturnedPlayer playerCaller)
            {
                SendMessage(playerCaller.Player, "Store secret key has been updated.");
            }
            Rocket.Core.Logging.Logger.Log($"Store secret key has been updated by {caller.DisplayName}.");
        }

        private void HandleWebResponse(UnturnedPlayer player, int code, string response)
        {
            if (string.IsNullOrEmpty(response) || code != 200)
            {
                Rocket.Core.Logging.Logger.LogWarning($"Failed to fetch transactions for {player.DisplayName}: {response ?? "No response"} (Code: {code})");
                SendMessage(player.Player, "API Services are currently offline. Please check back shortly.");
                return;
            }

            try
            {
                var transactions = JsonConvert.DeserializeObject<Transaction[]>(response);
                if (transactions != null && transactions.Length > 0)
                {
                    // Check for specific error message within the first transaction
                    if (!string.IsNullOrEmpty(transactions[0].message))
                    {
                        SendMessage(player.Player, transactions[0].message);
                        return;
                    }

                    ProcessTransactions(player, transactions);
                }
                else
                {
                    Rocket.Core.Logging.Logger.LogWarning("No transactions found in the response.");
                    SendMessage(player.Player, "An error occurred while processing your request. Please try again later.");
                }
            }
            catch (JsonException ex)
            {
                Rocket.Core.Logging.Logger.LogException(ex, "Error parsing JSON response");
                SendMessage(player.Player, "An error occurred while processing your request. Please try again later.");
            }
        }


        private void ProcessTransactions(UnturnedPlayer player, Transaction[] transactions)
        {
            foreach (var transaction in transactions)
            {
                if (transaction == null)
                {
                    SendMessage(player.Player, "Encountered a null transaction object.");
                    continue;
                }

                if (transaction.product_amount < 1)
                {
                    SendMessage(player.Player, $"Invalid product amount: {transaction.product_amount}");
                    continue;
                }

                ushort itemId;
                if (!ushort.TryParse(transaction.product_id_string, out itemId))
                {
                    SendMessage(player.Player, $"Invalid item ID: {transaction.product_id_string}");
                    continue;
                }

                player.GiveItem(itemId, (byte)transaction.product_amount);
                SendMessage(player.Player, $"Gave {transaction.product_amount} {transaction.product_name} to {player.DisplayName}.");
            }
        }

        private void SendMessage(Player player, string message)
        {
            ChatManager.serverSendMessage(message, Color.green, null, player.channel.owner, EChatMode.SAY, null, true);
        }

        private IEnumerator SendPostRequest(string url, string jsonData, string apiKey, UnturnedPlayer player)
        {
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-API-Key", apiKey);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Rocket.Core.Logging.Logger.LogWarning($"Error: {request.error}");
                SendMessage(player.Player, "An error occurred while processing your request.");
            }
            else
            {
                HandleWebResponse(player, (int)request.responseCode, request.downloadHandler.text);
            }
        }
    }

    public class TeamGamesConfiguration : IRocketPluginConfiguration
    {
        public string StoreSecretKey { get; set; }

        public void LoadDefaults()
        {
            StoreSecretKey = "default-key";
        }
    }

    public class Transaction
    {
        public string player_name { get; set; }
        public string product_id_string { get; set; }
        public int product_amount { get; set; }
        public string product_name { get; set; }
        public string message { get; set; }
    }
}
