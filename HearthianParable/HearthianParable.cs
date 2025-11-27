using HarmonyLib;//
using OWML.Common;//
using OWML.ModHelper;//
using System;//
using System.Collections.Generic;//
using System.IO;//
using System.Reflection;//
using Newtonsoft.Json;//
using UnityEngine;//
using UnityEngine.InputSystem;//

namespace HearthianParable;

public class HearthianParable : ModBehaviour {
    public static HearthianParable Instance;
    public INewHorizons NewHorizons;
    readonly GameObject[] layers = new GameObject[4];
    readonly Transform[] triggers = new Transform[3];
    readonly Dictionary<string, Transform> endVolumes = [];
    GravityVolume grav;
    float init_surfaceAcceleration, init_cutoffAcceleration, init_gravitationalMass;
    GameObject player, daTree;
    ShipLogManager shipLogManager;
    SubmitActionCloseMenu closeMenu, closeOptMenu;
    int subtitlesState = 0;
    DialogueBoxVer2 subtitles;
    ScreenPrompt speedrunPrompt;
    float speedrunTime, speedrunIGTime;
    AudioSource audioSource, devSource;
    string currentDevClip;
    float silenceTimer;
    readonly Dictionary<string, AudioClip> audioClips = [];
    readonly Dictionary<string, float> audioLength = [];
    readonly List<(float, Action)> actionsQueue = [];
    bool ambiantSoundsOn = true;
    bool dataLoaded = false;
    int gameState = 0, difficulty = 0;
    bool sawTree, landed, disappointed, holeFound, holeSaw, nomaiFound, upsideDown, sawSettings, heardDev, devCom, devSpedUp, devFound, reachedCore, speedRunTimer;
    string language = "english";
    Dictionary<string, Dictionary<string, string[]>> localization = [];
    readonly int[] dialoguesTimings = [0, 3, 4, 7, 11, 12, 16, 23, 30, 34, 37, 39, 45, 48, 53, 55, 666, 0, 2, 666, 0, 4, 666, 0, 666, 0, 666, 0, 666, 0, 666, 0, 666, 0, 10, 13, 16, 21, 666, 0, 3, 666, 0, 666, 0, 3, 666, 0, 5, 7, 666, 0, 666, 0, 666, 2, 5, 8, 12, 18, 666, 0, 4, 8, 11, 17, 21, 666, 0, 5, 7, 666, 0, 6, 14, 666, 0, 3, 5, 7, 10, 14, 16, 19, 23, 26, 666, 0, 4, 10, 14, 21, 27, 666, 0, 4, 10, 16, 24, 29, 33, 36, 666, 0, 9, 17, 30, 34, 40, 50, 56, 59, 69, 81, 83, 88, 91, 666, 0, 6, 15, 20, 30, 38, 45, 53, 59, 67, 78, 84, 94, 97, 106, 117, 129, 140, 143, 148, 155, 157, 159, 165, 666, 80, 86, 93, 101, 106, 110, 666, 0, 5, 13, 19, 22];

    public void Awake() {
        Instance = this;
        // You won't be able to access OWML's mod helper in Awake.
        // So you probably don't want to do anything here.
        // Use Start() instead.
    }

    public void Start() {
        // Starting here, you'll have access to OWML's mod helper.
        //ModHelper.Console.WriteLine($"My mod {nameof(HearthianParable)} is loaded!", MessageType.Success);

        // Get the New Horizons API and load configs
        NewHorizons = ModHelper.Interaction.TryGetModApi<INewHorizons>("xen.NewHorizons");
        NewHorizons.LoadConfigs(this);

        new Harmony("Vambok.HearthianParable").PatchAll(Assembly.GetExecutingAssembly());
        ModHelper.Events.Unity.RunWhen(PlayerData.IsLoaded, LoadData);
        LoadManager.OnStartSceneLoad += GetDialoguesLocalization;
        StandaloneProfileManager.SharedInstance.OnProfileReadDone += LoadData;
        OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen); // We start on title screen
        LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
        //NewHorizons.GetStarSystemLoadedEvent().AddListener(SpawnIntoSystem);
    }

    void LoadData() {
        if(PlayerData.IsLoaded()) {
            //ModHelper.Console.WriteLine("Data LOADED!", MessageType.Success);
            ShipLogFactSave saveData = PlayerData.GetShipLogFactSave("HearthlingParable_gameState");
            gameState = (saveData != null) ? int.Parse(saveData.id) : 0;
            saveData = PlayerData.GetShipLogFactSave("HearthlingParable_gameSettings");
            if(saveData != null) {
                int settings = int.Parse(saveData.id);
                devCom = (settings & 1) > 0;
                speedRunTimer = (settings & 2) > 0;
                difficulty = (settings >> 2);
            } else {
                devCom = false;
                speedRunTimer = false;
                difficulty = 0;
            }
            ModHelper.Config.SetSettingsValue("DevCom", devCom);
            ModHelper.Config.SetSettingsValue("Speedrun", speedRunTimer);
            ModHelper.Config.SetSettingsValue("Difficulty", difficulty switch {
                1 => "Normal: Subtitles",
                2 => "Hard: Only audio",
                3 => "Insane: Nothing",
                _ => "Easy: Shiplogs"
            });
            ModHelper.Config.SetSettingsValue("Mod", true);
            if(audioClips.Count <= 0) {
                AssetBundle audioBundle = AssetBundle.LoadFromFile(Path.Combine(ModHelper.Manifest.ModFolderPath, "Assets", "audiobundle"));
                if(audioBundle != null)
                    foreach(string name in audioBundle.GetAllAssetNames()) {
                        string shortName = name.Split('/')[2].Split('.')[0];
                        audioClips[shortName] = audioBundle.LoadAsset<AudioClip>(name);
                        audioLength[shortName] = audioClips[shortName].length;
                        //ModHelper.Console.WriteLine(shortName + " (" + name + ")", MessageType.Success);
                    }
                localization = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string[]>>>(File.ReadAllText(Path.Combine(ModHelper.Manifest.ModFolderPath, "translations.json")));
                foreach((string lang, Dictionary<string, string[]> dialect) in localization) {
                    if(lang == "english") continue;
                    string[] keys = new string[dialect.Count];
                    dialect.Keys.CopyTo(keys, 0);
                    foreach(string key in keys) {
                        string[] value = dialect[key];
                        string[] defaultArray = localization["english"][key];
                        if(value.Length < defaultArray.Length) {
                            string[] newArray = new string[defaultArray.Length];
                            Array.Copy(defaultArray, newArray, defaultArray.Length);
                            Array.Copy(value, newArray, value.Length);
                            dialect[key] = newArray;
                        }
                    }
                }
            }
            //GetDialoguesLocalization();
            dataLoaded = true;
            //Cleanup: How to?? that:
            //PlayerData._currentGameSave.shipLogFactSaves.Remove("VAM-THP_END1_RUM");
            //PlayerData._currentGameSave.shipLogFactSaves.Remove("VAM-THP_END2_RUM");
            //PlayerData._currentGameSave.shipLogFactSaves.Remove("VAM-THP_END3_RUM");
            //PlayerData._currentGameSave.shipLogFactSaves.Remove("VAM-THP_END4_RUM");
            //PlayerData._currentGameSave.shipLogFactSaves.Remove("VAM-THP_END5_RUM");
            //PlayerData._currentGameSave.shipLogFactSaves.Remove("VAM-THP_END_1");
        }
    }
    void SaveQuit() {
        actionsQueue.Clear();
        UpdateSubtitle(0);
        SaveState();
    }
    void SaveState() {
        PlayerData._currentGameSave.shipLogFactSaves["HearthlingParable_gameState"] = new ShipLogFactSave(gameState.ToString());
        PlayerData._currentGameSave.shipLogFactSaves["HearthlingParable_gameSettings"] = new ShipLogFactSave((difficulty * 4 + (speedRunTimer ? 2 : 0) + (devCom ? 1 : 0)).ToString());
        PlayerData.SaveCurrentGame();
    }
    void GetDialoguesLocalization(OWScene previousScene, OWScene newScene) {
        if(newScene != OWScene.SolarSystem) return;
        language = PlayerData.GetSavedLanguage() switch {
            TextTranslation.Language.SPANISH_LA => "spanish_la",
            TextTranslation.Language.GERMAN => "german",
            TextTranslation.Language.FRENCH => "french",
            TextTranslation.Language.ITALIAN => "italian",
            TextTranslation.Language.POLISH => "polish",
            TextTranslation.Language.PORTUGUESE_BR => "portuguese_br",
            TextTranslation.Language.JAPANESE => "japanese",
            TextTranslation.Language.RUSSIAN => "russian",
            TextTranslation.Language.CHINESE_SIMPLE => "chinese_simple",
            TextTranslation.Language.KOREAN => "korean",
            TextTranslation.Language.TURKISH => "turkish",
            _ => "english"
        };
        if(!localization.ContainsKey(language)) language = "english";
    }
    public override void Configure(IModConfig config) {
        if(dataLoaded) {
            GetSettings(config);
            SaveState();
            speedrunPrompt?.SetVisibility(speedRunTimer);
            //GetDialoguesLocalization();
            if(LoadManager.GetCurrentScene() == OWScene.SolarSystem) {
                if(!config.GetSettingsValue<bool>("Mod")) {
                    closeOptMenu?.Submit();
                    closeMenu?.Submit();
                    Ending("deactivated");
                }
                if(landed && !sawSettings && !devCom) {
                    sawSettings = true;
                    Narration("settings");
                }
            }
        }
    }
    void GetSettings(IModConfig config = null) {
        config ??= ModHelper.Config;
        devCom = config.GetSettingsValue<bool>("DevCom");
        speedRunTimer = config.GetSettingsValue<bool>("Speedrun");
        int old_difficulty = difficulty;
        difficulty = config.GetSettingsValue<string>("Difficulty") switch {
            "Normal: Subtitles" => 1,
            "Hard: Only audio" => 2,
            "Insane: Nothing" => 3,
            _ => 0
        };
        if(LoadManager.GetCurrentScene() == OWScene.SolarSystem) {
            if((!devCom && devSource != null && devSource.volume > 0.1f) || (difficulty > 1 && old_difficulty < 2)) UpdateSubtitle(0);
            else if(difficulty < 2 && old_difficulty > 1) UpdateSubtitle(subtitlesState - 1);
            else if(devCom && devSource != null && devSource.volume < 0.1f && devSource.isPlaying) SubtitlesManager(-1);
        }
    }

    public void OnCompleteSceneLoad(OWScene previousScene, OWScene newScene) {
        ModHelper.Config.SetSettingsValue("Mod", true);
        if(newScene != OWScene.SolarSystem) {
            if(speedrunPrompt != null && speedrunIGTime>0) speedrunIGTime -= Time.realtimeSinceStartup;
            return;
        }
        //GetSettings();
        landed = disappointed = holeFound = holeSaw = nomaiFound = upsideDown = sawSettings = heardDev = devSpedUp = devFound = reachedCore = false;
        SubmitActionLoadScene actionLoadScene = GameObject.Find("PauseMenuBlock").transform.Find("PauseMenuItems/PauseMenuItemsLayout/Button-ExitToMainMenu").GetComponent<SubmitActionLoadScene>();
        actionLoadScene.OnSubmitAction -= SaveQuit;
        actionLoadScene.OnSubmitAction += SaveQuit;
        closeMenu = GameObject.Find("PauseMenuBlock").transform.Find("PauseMenuItems/PauseMenuItemsLayout/Button-Unpause").GetComponent<SubmitActionCloseMenu>();
        closeOptMenu = GameObject.Find("PauseMenu/OptionsCanvas").transform.Find("OptionsMenu-Panel/OptionsButtons/UIElement-SaveAndExit").GetComponent<SubmitActionCloseMenu>();
        SpawnIntoSystem();
    }

    void SpawnIntoSystem(string systemName = "Jam5") {
        //ModHelper.Console.WriteLine("Spawn into Why??? system: " + (systemName != "Jam5") + (layers[1] != null), MessageType.Success);
        if(systemName != "Jam5" || (layers[1] != null && layers[1].GetComponent<Gravity_reverse>() != null)) return;
        ModHelper.Events.Unity.FireInNUpdates(() => {
            for(int i = 1;i < 6;i++) GameObject.Find("Vambok_THP_Platform_Body/Sector/Item" + i).SetActive((gameState & 1 << (i - 1)) > 0);
            daTree = GameObject.Find("Vambok_THP_Platform_Body/Sector/Treepot/TreeBad");
            if((gameState & 31) > 30) {
                GameObject.Find("Vambok_THP_Platform_Body/Sector/Treepot/TreeGood").SetActive(true);
                daTree.SetActive(false);
            } else {
                GameObject.Find("Vambok_THP_Platform_Body/Sector/Treepot/TreeGood").SetActive(false);
                daTree.SetActive(true);
            }
            if(speedrunPrompt == null) {
                speedrunPrompt = new ScreenPrompt("");
                speedrunTime = Time.realtimeSinceStartup;
            }
            if(speedrunIGTime <= 0) speedrunIGTime += Time.realtimeSinceStartup;
            Locator.GetPromptManager().AddScreenPrompt(speedrunPrompt, PromptPosition.UpperRight);
            speedrunPrompt.SetVisibility(speedRunTimer);
            shipLogManager = Locator.GetShipLogManager();
            subtitles = GameObject.FindWithTag("DialogueGui").GetRequiredComponent<DialogueBoxVer2>();
            player = GameObject.Find("Player_Body");
            audioSource = player.AddComponent<AudioSource>();
            devSource = player.AddComponent<AudioSource>();
            currentDevClip = "devcom";
            devSource.clip = audioClips[currentDevClip];
            if(devCom) SubtitlesManager(88);
            devSource.volume = ((devCom && difficulty < 3) ? 1 : 0);
            devSource.Play();
            layers[0] = NewHorizons.GetPlanet("Big_Little_Planet");
            layers[1] = layers[0].transform.Find("Sector/Layer1").gameObject;
            layers[2] = layers[0].transform.Find("Sector/Layer2").gameObject;
            layers[3] = layers[0].transform.Find("Sector/Layer3").gameObject;
            Destroy(layers[3].transform.Find("Line1").gameObject);
            triggers[0] = layers[0].transform.Find("Sector/endVolumes");
            foreach(Transform endVol in triggers[0].transform.GetComponentsInChildren<Transform>()) {
                endVolumes[endVol.gameObject.name] = endVol;
                SphereShape titi = endVol.GetComponent<SphereShape>();
                if(titi != null) titi.enabled = true;
            }
            triggers[1] = layers[3].transform.Find("rotato3");
            Destroy(triggers[1].GetComponent<MeshRenderer>());
            triggers[2] = NewHorizons.GetPlanet("Little_Big_Planet").transform.Find("Sector/Vambok");
            GameObject toto;
            Transform rotato;
            for(int j = 1;j < 4;j++) {
                layers[j].AddComponent<Gravity_reverse>().modInstance = Instance;
                toto = layers[j].transform.Find("ring" + j).gameObject;
                toto.AddComponent<MeshCollider>();
                rotato = layers[j].transform.Find("rotato" + j);
                for(int i = 0;i < 15;i++) {
                    Instantiate(toto, rotato, true);
                    rotato.localEulerAngles += new Vector3(6.3158f, 0, 0);
                }
                for(int i = 0;i < 28;i++) {
                    Instantiate(toto, rotato, true);
                    rotato.localEulerAngles -= new Vector3(6.3158f, 0, 0);
                }
                for(int i = 0;i < 14;i++) {
                    Instantiate(toto, rotato, true);
                    rotato.localEulerAngles += new Vector3(6.3158f, 0, 0);
                }
                foreach(MeshCollider toti in rotato.GetComponentsInChildren<MeshCollider>()) {
                    toti.enabled = true;
                }
                foreach(MeshRenderer toti in rotato.GetComponentsInChildren<MeshRenderer>()) {
                    toti.enabled = true;
                }
            }
            grav = layers[0].transform.Find("GravityWell").GetComponent<GravityVolume>();
            init_surfaceAcceleration = grav._surfaceAcceleration;
            init_cutoffAcceleration = grav._cutoffAcceleration = 2.4f;// gravity on inner layer = 12*groundSize/500
            init_gravitationalMass = grav._gravitationalMass = 1000f * 12 * 400 * 400;// 1000*surfaceGravity*surfaceSize^gravFallOff
            grav._lowerSurfaceRadius = 400;// = surfaceSize
            grav._cutoffRadius = 100;// = groundSize
        }, 91);
    }

    void SpeedrunTimer() {
        float speedrunTot = Time.realtimeSinceStartup - speedrunTime;
        float speedrunIGTot = Time.realtimeSinceStartup - speedrunIGTime;
        int speedrunMins = (int)(speedrunTot / 60);
        int speedrunIGMins = (int)(speedrunIGTot / 60);
        speedrunPrompt.SetText(localization[language]["speedrunTimer"][0] + " " + speedrunMins + localization[language]["speedrunTimer"][2] + (speedrunTot - speedrunMins * 60).ToString("f") + localization[language]["speedrunTimer"][3] + "\n" + localization[language]["speedrunTimer"][1] + " " + speedrunIGMins + localization[language]["speedrunTimer"][2] + (speedrunIGTot - speedrunIGMins * 60).ToString("f") + localization[language]["speedrunTimer"][3]);
    }
    void GlobalTriggers() {
        // Tree start:
        if(!sawTree && (player.transform.position - daTree.transform.position).magnitude < 10) {
            sawTree = true;
            shipLogManager.RevealFact("VAM-THP_ROOT_RUM");
        }
        //
        float planet_dist = (player.transform.position - layers[0].transform.position).magnitude;
        // Anti-ship shield:
        if(planet_dist < 400 && PlayerState.IsInsideShip()) {
            Transform ship = Locator.GetShipTransform();
            ship.position += (ship.position - layers[0].transform.position).normalized * (400 - planet_dist);
        }
        // Layer's rotations:
        if(planet_dist < 370 && planet_dist > 190) {
            if(planet_dist > 310) {
                layers[1].transform.localEulerAngles += (Vector3.up - Vector3.forward) * 5 * Time.deltaTime;
                layers[3].transform.localEulerAngles -= Vector3.forward * 5 * Time.deltaTime;
            } else {
                layers[2].transform.localEulerAngles += (Vector3.forward - Vector3.up) * 5 * Time.deltaTime;
                layers[3].transform.localEulerAngles -= Vector3.up * 5 * Time.deltaTime;
            }
        } else {
            layers[1].transform.localEulerAngles += Vector3.up * 5 * Time.deltaTime;
            layers[2].transform.localEulerAngles += Vector3.forward * 5 * Time.deltaTime;
        }
        // V key gravity inversion:
        if(Keyboard.current.vKey.wasPressedThisFrame) {
            Gravity_reverse(0);
        }
        // Dev com K speedup:
        if(devCom) {
            if(!heardDev) heardDev = true;
            if(devSpedUp) {
                if(devSource.isPlaying) {
                    if(Keyboard.current.kKey.wasReleasedThisFrame && devSource.time < 80) {
                        devSource.time = 80;
                    }
                } else {
                    devCom = false;
                    ModHelper.Config.SetSettingsValue("DevCom", devCom);
                    Ending("kickedOut");
                }
            } else {
                if(!devFound && Keyboard.current.kKey.wasPressedThisFrame && devSource.isPlaying) {
                    float currentTime = (devSource.time + (landed ? 33.44f : 0) + (holeFound ? 38.33f : 0) + (reachedCore ? 95.28f : 0)) / 4;
                    if(currentTime < 78) {
                        devSpedUp = true;
                        devSource.Stop();
                        if(difficulty > 2) return;
                        currentDevClip = "devcomfast";
                        devSource.clip = audioClips[currentDevClip];
                        SubtitlesManager(144);
                        devSource.Play();
                        devSource.time = currentTime;
                    }
                }
            }
        }
        // Events:
        if(!landed) {// First landing:
            if(planet_dist < 410) {
                landed = true;
                Narration("landing");
            }
        } else if(!holeFound) {// Hole found:
            if((triggers[1].position + Vector3.right * 400 - player.transform.position).magnitude < 100) {
                holeFound = true;
                Narration("hole");
            }
        } else if(!nomaiFound) {// Floors found:
            if((triggers[1].position + Vector3.right * 400 - player.transform.position).magnitude < 35) {
                nomaiFound = true;
                Narration("nomaiFloors");
            }
        } else if(!upsideDown) {// Under surface:
            if(planet_dist < 400 && (triggers[1].position + Vector3.right * 400 - player.transform.position).magnitude > 35 && grav._cutoffAcceleration < 0) {
                upsideDown = true;
                Narration("innerSide");
            }
        }
        // Dead dev found:
        if(!devFound && (triggers[2].position - player.transform.position).magnitude < 5) {
            devFound = true;
            Narration("devFound");
        }
        // Reached planet's core:
        if(!reachedCore && planet_dist < 110) {
            reachedCore = true;
            Narration("planetCore");
        }
    }
    void GlobalAudioManagers() {
        if(devSource != null) {
            devSource.volume = (devCom && !audioSource.isPlaying ? 1 : 0);
            if(!OWTime.IsPaused(OWTime.PauseType.Menu)) {
                if(audioSource.isPlaying || (devCom && devSource.isPlaying)) {
                    if(ambiantSoundsOn) {
                        Locator.GetAudioMixer()._inGameVolume.FadeTo(0.5f, 1f);
                        ambiantSoundsOn = false;
                    }
                } else {
                    if(!ambiantSoundsOn) {
                        Locator.GetAudioMixer()._inGameVolume.FadeTo(1f, 1f);
                        ambiantSoundsOn = true;
                    }
                }
            }
        }
    }
    void Update() {
        if(layers[0] != null) {
            SpeedrunTimer();
            GlobalTriggers();
            GlobalAudioManagers();
            SubtitlesManager();
            if(actionsQueue.Count > 0 && Time.realtimeSinceStartup > actionsQueue[0].Item1)
                actionsQueue[0].Item2();
        }
    }

    void Narration(string audioId) {
        //ModHelper.Console.WriteLine(audioId + " playing", MessageType.Success);
        audioSource.Stop();
        actionsQueue.Clear();
        if(difficulty > 2) {
            if(audioId == "planetCore" && !heardDev) Ending("cheater");
            else if(audioId == "devFound") Ending(devCom ? "ernestoDev" : "ernesto");
            return;
        }
        switch(audioId) {
        case "landing":
            if(!devCom) {
                audioSource.clip = audioClips["landing"];
                SubtitlesManager(1);
                actionsQueue.Add((Time.realtimeSinceStartup + 2, () => { disappointed = true; actionsQueue.RemoveAt(0); }));
                audioSource.Play();
            } else SubtitlesManager(95);
            devSource.Stop();
            currentDevClip = "devlanding";
            devSource.clip = audioClips[currentDevClip];
            devSource.Play();
            break;
        case "hole":
            if(!devCom) {
                if(disappointed) {
                    audioSource.clip = audioClips["holea"];
                    SubtitlesManager(21);
                    actionsQueue.Add((Time.realtimeSinceStartup + audioLength["holea"], () => { Narration("hole2"); }));
                } else {
                    audioSource.clip = audioClips["hole"];
                    SubtitlesManager(18);
                    actionsQueue.Add((Time.realtimeSinceStartup + audioLength["hole"], () => { Narration("hole2"); }));
                }
                audioSource.Play();
            } else SubtitlesManager(104);
            devSource.Stop();
            currentDevClip = "devhole";
            devSource.clip = audioClips[currentDevClip];
            devSource.Play();
            break;
        case "hole2":
            if(!devCom) {
                audioSource.clip = audioClips["hole2"];
                SubtitlesManager(24);
                actionsQueue.Add((Time.realtimeSinceStartup + 2.6f, () => { holeSaw = true; actionsQueue.RemoveAt(0); }));
                audioSource.Play();
                if(disappointed) actionsQueue.Add((Time.realtimeSinceStartup + audioLength["hole2"], () => { Narration("hole2A"); }));
            }
            break;
        case "hole2A":
            if(!devCom) {
                audioSource.clip = audioClips["hole2a"];
                SubtitlesManager(26);
                audioSource.Play();
            }
            break;
        case "settings":
            if(!devCom) {
                if(nomaiFound) {
                    audioSource.clip = audioClips["settingsa"];
                    SubtitlesManager(40);
                } else {
                    audioSource.clip = audioClips["settings"];
                    SubtitlesManager(28);
                    actionsQueue.Add((Time.realtimeSinceStartup + audioLength["settings"], () => { Narration("settings2"); }));
                }
                audioSource.Play();
            }
            break;
        case "settings2":
            if(!devCom) {
                if(holeSaw) {
                    audioSource.clip = audioClips["settings2a"];
                    SubtitlesManager(32);
                    actionsQueue.Add((Time.realtimeSinceStartup + audioLength["settings2a"], () => { Narration("settings3"); }));
                } else {
                    audioSource.clip = audioClips["settings2"];
                    SubtitlesManager(30);
                    actionsQueue.Add((Time.realtimeSinceStartup + audioLength["settings2"], () => { Narration("settings3"); }));
                }
                audioSource.Play();
            }
            break;
        case "settings3":
            if(!devCom) {
                audioSource.clip = audioClips["settings3"];
                SubtitlesManager(34);
                audioSource.Play();
            }
            break;
        case "nomaiFloors":
            nomaiFound = true;
            if(!devCom) {
                if(holeSaw) {
                    audioSource.clip = audioClips["nomaia"];
                    SubtitlesManager(45);
                } else {
                    audioSource.clip = audioClips["nomai"];
                    SubtitlesManager(43);
                }
                audioSource.Play();
            }
            break;
        case "innerSide":
            if(!devCom) {
                audioSource.clip = audioClips["innerside"];
                SubtitlesManager(48);
                actionsQueue.Add((Time.realtimeSinceStartup + audioLength["innerside"], () => { Narration("innerSide2"); }));
                audioSource.Play();
            }
            break;
        case "innerSide2":
            if(!devCom) {
                if(sawSettings) {
                    audioSource.clip = audioClips["innerside2a"];
                    SubtitlesManager(54);
                } else {
                    audioSource.clip = audioClips["innerside2"];
                    SubtitlesManager(52);
                }
                audioSource.Play();
            }
            break;
        case "planetCore":
            audioSource.clip = audioClips["core"];
            SubtitlesManager(56);
            actionsQueue.Add((Time.realtimeSinceStartup + audioLength["core"], () => { Narration("planetCore2"); }));
            audioSource.Play();
            break;
        case "planetCore2":
            if(heardDev) {
                if(devCom) {
                    audioSource.clip = audioClips["core1"];
                    SubtitlesManager(62);
                    actionsQueue.Add((Time.realtimeSinceStartup + audioLength["core1"], () => { Narration("devCore"); }));
                } else {
                    audioSource.clip = audioClips["core2"];
                    SubtitlesManager(69);
                    devSource.Stop();
                    currentDevClip = "devcore";
                    devSource.clip = audioClips[currentDevClip];
                    devSource.Play();
                }
            } else {
                audioSource.clip = audioClips["core3"];
                SubtitlesManager(73);
                actionsQueue.Add((Time.realtimeSinceStartup + audioLength["core3"], () => { Ending("cheater"); }));
            }
            audioSource.Play();
            break;
        case "devCore":
            devSource.Stop();
            currentDevClip = "devcore";
            devSource.clip = audioClips[currentDevClip];
            SubtitlesManager(119);
            devSource.Play();
            break;
        case "devFound":
            if(devCom) {
                devSource.Stop();
                currentDevClip = "devdead";
                devSource.clip = audioClips[currentDevClip];
                SubtitlesManager(151);
                devSource.Play();
                actionsQueue.Add((Time.realtimeSinceStartup + audioLength["devdead"], () => { Ending("ernestoDev"); }));
            } else {
                audioSource.clip = audioClips["devfound"];
                SubtitlesManager(77);
                actionsQueue.Add((Time.realtimeSinceStartup + audioLength["devfound"], () => { Ending("ernesto"); }));
                audioSource.Play();
            }
            break;
        default:
            return;
        }
    }

    public void Gravity_reverse(int factor) {
        if(factor == 0) {
            grav._surfaceAcceleration = -Mathf.Sign(grav._surfaceAcceleration) * init_surfaceAcceleration;
            grav._cutoffAcceleration = -Mathf.Sign(grav._cutoffAcceleration) * init_cutoffAcceleration;
            grav._gravitationalMass = -Mathf.Sign(grav._gravitationalMass) * init_gravitationalMass;
        } else {
            grav._surfaceAcceleration += factor * init_surfaceAcceleration;
            grav._cutoffAcceleration += factor * init_cutoffAcceleration;
            grav._gravitationalMass += factor * init_gravitationalMass;
        }
    }

    void Ending(string type) {
        string factUnlocked = "";
        switch(type) {
        case "deactivated":
            factUnlocked = "VAM-THP_END5_FACT";
            gameState |= 1 << 4;
            break;
        case "ernestoDev":
            factUnlocked = "VAM-THP_END1_FACT";
            shipLogManager.GetFact("VAM-THP_END2_1")._save.read = false;
            shipLogManager.GetFact("VAM-THP_END2_1")._save.newlyRevealed = true;
            gameState |= 1 << 3;
            break;
        case "ernesto":
            factUnlocked = "VAM-THP_END2_FACT";
            shipLogManager.GetFact("VAM-THP_END1_1")._save.read = false;
            shipLogManager.GetFact("VAM-THP_END1_1")._save.newlyRevealed = true;
            gameState |= 1 << 2;
            break;
        case "cheater":
            factUnlocked = "VAM-THP_END4_FACT";
            gameState |= 1 << 1;
            break;
        case "kickedOut":
            factUnlocked = "VAM-THP_END3_FACT";
            gameState |= 1 << 0;
            break;
        default: break;
        }
        shipLogManager.RevealFact(factUnlocked);
        if((gameState & 31) > 30) shipLogManager.RevealFact("VAM-THP_ROOT_FACT");
        SaveQuit();
        if(type == "ernesto" && PlayerData.GetPersistentCondition("VAMBOK_THP_KNOWS_TRUTH")) type = "ernestine";
        endVolumes[type].position = player.transform.position;
        endVolumes[type].parent = player.transform;
        endVolumes[type].GetComponent<SphereShape>().enabled = true;
    }

    void SubtitlesManager(int inState = 0) {
        if(inState < 0 && currentDevClip != null) {
            switch(currentDevClip) {
            case "devcom":
                UpdateSubtitle(88);
                break;
            case "devcomfast":
                UpdateSubtitle(144);
                break;
            case "devlanding":
                UpdateSubtitle(95);
                break;
            case "devhole":
                UpdateSubtitle(104);
                break;
            case "devcore":
                UpdateSubtitle(119);
                break;
            case "devdead":
                UpdateSubtitle(151);
                break;
            default:
                break;
            }
        } else {
            if(inState > 0) subtitlesState = inState;
            if(subtitlesState > 0) {
                if(audioSource.isPlaying || (devCom && devSource.isPlaying)) {
                    /*if(Time.realtimeSinceStartup > test) {
                        ModHelper.Console.WriteLine((dialogues[subtitlesState - 1] == "") + " " + devCom + " " + currentDevClip, MessageType.Success);
                        test++;
                    }*/
                    if((audioSource.isPlaying ? audioSource.time : devSource.time) > dialoguesTimings[subtitlesState - 1]) {
                        if(difficulty < 2) UpdateSubtitle(subtitlesState);
                        subtitlesState++;
                        if(subtitlesState > 155) subtitlesState = 0;
                    }
                } else if((localization[language]["dialogues"][subtitlesState - 1] == "") || ((Time.realtimeSinceStartup < silenceTimer + 0.4f) && (Time.realtimeSinceStartup > silenceTimer + 0.2f))) {
                    UpdateSubtitle(0);
                } else if(Time.realtimeSinceStartup > silenceTimer + 0.4f) silenceTimer = Time.realtimeSinceStartup;
            }
        }
    }
    void UpdateSubtitle(int state) {
        if(state > 0) {
            subtitles._potentialOptions = null;
            subtitles.ResetAllText();
            subtitles.SetNameFieldVisible(false);
            subtitles.SetMainFieldDialogueText(localization[language]["dialogues"][state - 1]);
            subtitles._buttonPromptElement.gameObject.SetActive(false);
            subtitles._mainFieldTextEffect?.StartTextEffect();
            if(difficulty < 1) SubtitleShipLogs(state);
            subtitlesState = state;
        } else {
            subtitles.SetVisible(false);
            subtitlesState = 0;
        }
    }
    void SubtitleShipLogs(int state) {
        switch(state) {
        case 12:
        case 50:
            shipLogManager.RevealFact("VAM-THP_END5_1");
            break;
        case 101:
            shipLogManager.RevealFact("VAM-THP_END4_1");
            break;
        case 43:
        case 45:
        case 104:
            shipLogManager.RevealFact("VAM-THP_END4_1");
            shipLogManager.RevealFact("VAM-THP_END4_3");
            break;
        case 110:
            shipLogManager.RevealFact("VAM-THP_END4_2");
            shipLogManager.RevealFact("VAM-THP_END3_1");
            break;
        case 70:
            shipLogManager.RevealFact("VAM-THP_END3_2");
            shipLogManager.RevealFact("VAM-THP_END4_2");
            shipLogManager.RevealFact("VAM-THP_END4_4");
            break;
        case 63:
            shipLogManager.RevealFact("VAM-THP_END3_2");
            shipLogManager.RevealFact("VAM-THP_END3_3");
            break;
        case 135:
            shipLogManager.RevealFact("VAM-THP_END1_1");
            shipLogManager.RevealFact("VAM-THP_END2_1");
            break;
        default:
            break;
        }
    }
}

public class Gravity_reverse : MonoBehaviour {
    public HearthianParable modInstance;
    private void OnTriggerEnter(Collider col) {
        if(col.CompareTag("Player")) modInstance.Gravity_reverse(-2);
    }
    private void OnTriggerExit(Collider col) {
        if(col.CompareTag("Player")) modInstance.Gravity_reverse(2);
    }
}

// ambiant sound down when audio
