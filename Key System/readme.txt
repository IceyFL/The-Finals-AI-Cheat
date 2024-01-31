add this function to mainwindow.xaml.cs

        private async Task<bool> CheckKey()
        {
            string key;
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string keyFilePath = Path.Combine(appDataFolder, "windir.rnd");
            if (File.Exists(keyFilePath))
            {
                key = File.ReadAllText(keyFilePath);
            }
            else { key = Microsoft.VisualBasic.Interaction.InputBox("Enter Key:", "Key System", ""); }
            bool validkey = false;
            while (validkey == false)
            {
                if (key == "")
                {
                    return false;
                }
                else
                {
                    try
                    {
                        string key10 = key.Substring(0, Math.Min(10, key.Length));
                        long longkey = long.Parse(key);
                        string url = "https://jsonblob.com/api/";
                        using (HttpClient client = new HttpClient())
                        {
                            try
                            {
                                HttpResponseMessage response = await client.GetAsync(url);

                                if (response.IsSuccessStatusCode)
                                {
                                    string jsonResponse = await response.Content.ReadAsStringAsync();
                                    JObject json = JObject.Parse(jsonResponse);
                                    JArray keysArray = json["keys"] as JArray;
                                    if (keysArray != null)
                                    {
                                        long[] keys = keysArray.Select(key => (long)key).ToArray();
                                        foreach (var curkey in keys)
                                        {
                                            if (curkey == longkey)
                                            {
                                                if (key.Length == 10 && (int.Parse(key10) > DateTimeOffset.UtcNow.ToUnixTimeSeconds())) { File.WriteAllText(keyFilePath, key); }
                                                else
                                                {
                                                    int keytime = int.Parse(key.Substring(10));
                                                    int genkey = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                                    genkey = genkey + (keytime * 86400);
                                                    File.WriteAllText(keyFilePath, genkey.ToString());
                                                    int indexToRemove = keysArray.Select((k, index) => new { k, index })
                                                    .FirstOrDefault(item => (long)item.k == longkey)?.index ?? -1;
                                                    if (indexToRemove != -1)
                                                    {
                                                        // Remove the key from the array
                                                        keysArray.RemoveAt(indexToRemove);
                                                        keysArray.Add(genkey);

                                                        // Update the JSON blob with the modified array
                                                        json["keys"] = keysArray;

                                                        // Convert the updated JSON to string and write to file

                                                        try
                                                        {
                                                            string url2 = "https://jsonblob.com/api/1198637883778260992"; // Replace with your actual JSONBlob ID

                                                            using (HttpClient client2 = new HttpClient())
                                                            {
                                                                // Create a StringContent object with the JSON data
                                                                StringContent content = new StringContent(json.ToString(), System.Text.Encoding.UTF8, "application/json");

                                                                // Send a PUT request to update the JSONBlob content
                                                                HttpResponseMessage response2 = await client2.PutAsync(url2, content);

                                                                // Check if the request was successful
                                                                return response2.IsSuccessStatusCode;
                                                            }
                                                        }
                                                        catch (HttpRequestException ex)
                                                        {
                                                            Console.WriteLine($"Exception: {ex.Message}");
                                                        }
                                                    }
                                                }
                                                return true;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                                }
                            }
                            catch (HttpRequestException ex)
                            {
                                Console.WriteLine($"Exception: {ex.Message}");
                            }
                        };
                    }
                    catch (Exception) { }
                    key = Microsoft.VisualBasic.Interaction.InputBox("Key invalid or expired:", "Key System", "");
                }

            }
            return false;
        }




then modify initializemodel




        private async void InitializeModel()
        {
            bool result = await CheckKey();
            if (result)
            {
                try
                {
                    if (!ModelLoadDebounce)
                    {
                        if (!(Bools.AIAimAligner))
                        {
                            ModelLoadDebounce = true;

                            // Save the embedded resource to a temporary file
                            string tempFilePath = Path.Combine(Path.GetTempPath(), "load.onnx");
                            SaveEmbeddedResourceToFile("Spotify.load.onnx", tempFilePath);

                            // Load the model from the temporary file
                            _onnxModel?.Dispose();
                            _onnxModel = new AIModel(tempFilePath)
                            {
                                ConfidenceThreshold = (float)(PasterSettings["AI_Min_Conf"] / 100.0f),
                                FovSize = (int)PasterSettings["FOV_Size"]
                            };

                            // Load the model from the temporary file
                            lastLoadedModel = "sup";
                            ModelLoadDebounce = false;
                            File.Delete(tempFilePath);
                        }
                    }
                }
                catch (Exception)
                {
                    System.Windows.MessageBox.Show("Installation is corrupt. Please create a Ticket on discord.", "Load Error");
                }
            }
        }



use newkey.py to generate a key
