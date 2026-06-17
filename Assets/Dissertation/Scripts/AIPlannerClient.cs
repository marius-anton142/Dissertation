using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AIPlannerClient : MonoBehaviour
{
    public string apiKeyPath = "Dissertation/Secrets/openai_key.txt";
    public string observationFilePath = "Dissertation/AI/ai_observation.json";
    public string commandsFilePath = "Dissertation/AI/ai_commands.json";

    public string model = "gpt-4o";
    public float temperature = 0f;

    public AIObservationBuilder observationBuilder;
    public AIMemoryManager memoryManager;

    public float lastApiLatencySeconds = 0f;
    public bool lastRequestSucceeded = false;

    public DungeonGenerationScript01 dungeonManager;
    public AIRagRetriever ragRetriever;

    public int lastPromptTokens = 0;
    public int lastCompletionTokens = 0;
    public int lastTotalTokens = 0;
    public string lastThought = "";

    private int lastPlayerX = -999;
    private int lastPlayerY = -999;
    public int stuckCounter = 0;

    public string lastPrompt = "";
    public string promptDebugFilePath = "Dissertation/AI/debug_last_prompt.txt";

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            StartCoroutine(RequestPlan());
        }
    }

    public IEnumerator RequestPlan()
    {
        string apiKeyFullPath = Path.Combine(Application.dataPath, apiKeyPath);
        string observationFullPath = Path.Combine(Application.dataPath, observationFilePath);
        string commandsFullPath = Path.Combine(Application.dataPath, commandsFilePath);

        if (File.Exists(commandsFullPath))
        {
            File.Delete(commandsFullPath);
        }

        if (!File.Exists(apiKeyFullPath))
        {
            Debug.LogError("API key file not found: " + apiKeyFullPath);
            yield break;
        }

        if (!File.Exists(observationFullPath))
        {
            Debug.LogError("Observation file not found: " + observationFullPath);
            yield break;
        }

        string apiKey = File.ReadAllText(apiKeyFullPath).Trim();
        string observationJson = File.ReadAllText(observationFullPath);

        AIObservation parsedObs = JsonUtility.FromJson<AIObservation>(observationJson);

        if (parsedObs != null)
        {
            if (parsedObs.playerX == lastPlayerX &&
                parsedObs.playerY == lastPlayerY &&
                parsedObs.recentActions != null &&
                parsedObs.recentActions.Count > 0)
            {
                stuckCounter++;
            }
            else
            {
                stuckCounter = 0;
                lastPlayerX = parsedObs.playerX;
                lastPlayerY = parsedObs.playerY;
            }
        }

        string reflexionSection = "";
        if (stuckCounter > 0)
        {
            reflexionSection =
                "CRITICAL WARNING:\n" +
                "- Your last action sequence FAILED because you bumped into a wall, tree, or closed door.\n" +
                "- You are stuck at the exact same position.\n" +
                "- DO NOT repeat the same Move actions. You MUST choose a different path.\n\n";
        }

        string memoryJson = "{}";

        bool useMemory =
            dungeonManager != null &&
            memoryManager != null &&
            observationBuilder != null &&
            dungeonManager.experimentMethod != AIExperimentMethod.PromptOnly;

        bool useGraphMemory =
            dungeonManager != null &&
            (
                dungeonManager.experimentMethod == AIExperimentMethod.PromptMemoryGraph ||
                dungeonManager.experimentMethod == AIExperimentMethod.PromptMemoryGraphRag
            );

        bool useRag =
            dungeonManager != null &&
            dungeonManager.experimentMethod == AIExperimentMethod.PromptMemoryGraphRag;

        if (useMemory)
        {
            AIObservation memoryObs = observationBuilder.BuildObservation();

            if (dungeonManager.experimentMethod == AIExperimentMethod.PromptMemory)
            {
                memoryJson = memoryManager.GetObjectsOnlyMemoryJson();
            }
            else
            {
                memoryJson = memoryManager.GetPromptMemoryJson(memoryObs.currentRoomId);
            }
        }

        string methodInstruction = "";

        if (dungeonManager != null)
        {
            switch (dungeonManager.experimentMethod)
            {
                case AIExperimentMethod.PromptOnly:
                    methodInstruction =
                        "Memory is disabled for this run. Use only the current observation.\n";
                    break;

                case AIExperimentMethod.PromptMemory:
                    methodInstruction =
                        "Memory contains only previously observed objects. It does not contain route hints or unexplored door hints.\n";
                    break;

                case AIExperimentMethod.PromptMemoryGraph:
                    methodInstruction =
                        "Memory contains known objects, navigation hints, and unexplored door hints.\n";
                    break;

                case AIExperimentMethod.PromptMemoryGraphRag:
                    methodInstruction =
                        "Memory contains known objects, navigation hints, unexplored door hints, and retrieved gameplay rules.\n";
                    break;

                case AIExperimentMethod.OracleCustom:
                    methodInstruction =
                        "This method is controlled by a scripted baseline, not by the LLM.\n";
                    break;
            }
        }

        bool isRagMode =
            dungeonManager != null &&
            dungeonManager.experimentMethod == AIExperimentMethod.PromptMemoryGraphRag;

        string outputRulesSection =
            "Output rules:\n" +
            "- Return between 1 and 4 actions in the actions array.\n" +
            "- Return fewer than 4 actions if the next action may change the room, pick up an item, cut a tree, open a door, enter a hazard, or require a new observation.\n" +
            "- If an action steps onto D or Z, make it the final action in the list because the game will auto-traverse the corridor and a new observation is needed.\n" +
            "- If an action steps onto A, make it the final action in the list because inventory changes and a new observation is needed.\n" +
            "- If an action steps onto C, make it the final action in the list because the player may become trapped and a new observation is needed.\n" +
            "- If an action uses a weapon, make it the final action in the list because the world may change.\n" +
            "- Use only actions from availableActions.\n" +
            "- Do not explain your answer.\n" +
            "- Prefer short safe action sequences. Do not include actions after entering a door, picking up an axe, entering a hazard, or attacking a tree.\n" +
            "- When moving across ordinary O floor toward a visible target, return up to 4 safe movement actions instead of only one.\n" +
            "- Do not stop after one movement if the next movement is still on O and continues toward the same target.\n" +
            "- Stop early only before stepping onto A, C, D, Z, or before using a weapon.\n\n";

        string mapLegendSection = "";

        if (isRagMode)
        {
            mapLegendSection =
                "Map legend:\n" +
                "- O = walkable floor.\n" +
                "- # = blocked or outside the room.\n" +
                "- P = player.\n" +
                "- T = tree.\n" +
                "- A = axe.\n" +
                "- D = closed door.\n" +
                "- Z = open door.\n" +
                "- C = cobweb hazard.\n" +
                "- Use retrieved gameplay rules for detailed mechanics of objects, hazards, enemies, and special tiles.\n\n";
        }
        else
        {
            mapLegendSection =
                "Map legend:\n" +
                "- O = walkable floor.\n" +
                "- # = blocked or outside the room.\n" +
                "- P = player.\n" +
                "- T = tree. It blocks movement. Cut it only from an adjacent tile with UseWeaponDirection.\n" +
                "- A = axe on the floor. Move onto A to pick it up.\n" +
                "- D = closed door. Move onto D to open it and enter the corridor.\n" +
                "- Z = open door. Move onto Z to enter the corridor.\n" +
                "- C = cobweb hazard. It is walkable but risky.\n\n";
        }

        string localStrategySection = "";

        if (isRagMode)
        {
            localStrategySection =
                "Local strategy priority:\n" +
                "1. Use the current observation as the source of truth for visible objects, doors, hazards, and the player position.\n" +
                "2. Use retrieved gameplay rules for detailed interaction mechanics.\n" +
                "3. If you do not have an axe, ignore visible trees AND memory hints for trees completely. Your ONLY priority is to explore unexplored doors to find an axe.\n" + // <--- ADDED HERE
                "4. Prioritize actions that make progress toward cutting one tree.\n" +
                "5. If no useful object is visible or reachable in the current room, explore through a visible door or follow memory hints.\n\n";
        }
        else
        {
            localStrategySection =
                "Local strategy priority:\n" +
                "1. If you have an axe selected and are immediately adjacent to a visible tree, use the weapon toward the tree.\n" +
                "2. If you have an axe but are not adjacent to a visible tree, move toward a walkable tile adjacent to the tree.\n" +
                "3. If you do not have an axe, ignore visible trees AND memory hints for trees completely. Your ONLY priority is to explore unexplored doors to find an axe.\n" +
                "4. If an axe is visible and you do not have one, move toward A and step onto it.\n" +
                "5. Avoid C unless there is no safe ordinary O route, or unless you are already stuck and must escape.\n" +
                "6. If no useful object is visible or reachable in the current room, explore through a visible door.\n\n";
        }

        string memoryStrategySection = "";

        if (dungeonManager != null &&
                    dungeonManager.experimentMethod == AIExperimentMethod.PromptMemory)
        {
            memoryStrategySection =
                "Memory strategy:\n" +
                "- Known world memory may contain objects observed in previous rooms or previous steps.\n" +
                "- If a needed object is known in memory but is not visible now, remember that it exists, but you do not have route hints.\n" +
                "- Without route hints, explore through visible doors when the needed object is not visible.\n\n";
        }
        else if (useGraphMemory)
        {
            memoryStrategySection =
                "Memory strategy:\n" +
                "- NEVER follow a navigationHint for a tree if you do not have an axe. Trees are useless without an axe.\n" +
                "- If an axe is known in memory but not visible and you do not have one, use navigationHints for targetType axe.\n" +
                "- If you do not have an axe and no axe is visible or known, use unexploredDoorHints to explore a new door.\n" +
                "- If you have an axe but no tree is visible, use navigationHints for targetType tree.\n" +
                "- If you have an axe and no tree is visible or known, use unexploredDoorHints to explore a new door.\n" +
                "- If both navigationHints and unexploredDoorHints exist, prefer navigationHints only when they point to an object you can currently use.\n\n";
        }

        string memoryRulesSection = "";

        if (dungeonManager != null &&
            dungeonManager.experimentMethod == AIExperimentMethod.PromptMemory)
        {
            memoryRulesSection =
                "Memory rules:\n" +
                "- knownObjects lists objects that still exist somewhere in the dungeon.\n" +
                "- knownObjects may include objects that are not visible in the current room.\n" +
                "- knownObjects does not provide a path. Use current visible doors to explore when needed.\n\n";
        }
        else if (useGraphMemory)
        {
            memoryRulesSection =
                "Memory rules:\n" +
                "- knownObjects lists objects that still exist somewhere in the dungeon.\n" +
                "- navigationHints are short route hints to known objects in other rooms. Each hint only gives the next door to use from the current room.\n" +
                "- unexploredDoorHints lists doors in the current room that do not yet have a known connection.\n" +
                "- If you need a known object in another room, use the matching navigationHint and move toward its doorWorldX/doorWorldY.\n" +
                "- If you need to explore, use unexploredDoorHints and move toward its doorWorldX/doorWorldY.\n" +
                "- Avoid moving back and forth between already connected rooms unless navigationHints explicitly require it.\n\n";
        }

        string ragRulesSection = "";

        string retrievedRulesJson = "{}";

        if (useRag && ragRetriever != null)
        {
            AIObservation ragObservation = observationBuilder.BuildObservation();

            yield return StartCoroutine(
                ragRetriever.RetrieveRules(
                    ragObservation,
                    memoryJson,
                    result => retrievedRulesJson = result
                )
            );

            Debug.Log("Retrieved rules inserted into prompt:");
            Debug.Log(retrievedRulesJson);

            ragRulesSection =
                "Retrieved gameplay rules:\n" +
                retrievedRulesJson + "\n\n" +
                "RAG rules:\n" +
                "- Retrieved gameplay rules contain detailed mechanics for currently relevant objects, hazards, enemies, or special tiles.\n" +
                "- Use retrieved rules when they apply to visible symbols or visible objects in the current observation.\n" +
                "- Retrieved rules override the minimal map legend when they provide more specific mechanics.\n\n";
        }

        string doorRulesSection = "";

        if (isRagMode)
        {
            doorRulesSection =
                "Door rules:\n" +
                "- Use visibleDoors as the source of truth for exact door positions.\n" +
                "- A door can only be entered by moving onto its exact row and col from visibleDoors.\n" +
                "- If a memory hint gives doorWorldX/doorWorldY, match it to a visibleDoor with the same worldX/worldY, then move toward that visibleDoor's row/col.\n" +
                "- Do not move beside a door. Move onto the exact D or Z tile.\n\n";
        }
        else
        {
            doorRulesSection =
                "Door rules:\n" +
                "- When exploring or navigating, choose a door from visibleDoors.\n" +
                "- A door can only be entered by moving onto its exact row and col from visibleDoors.\n" +
                "- Never move toward a wall tile next to a door. Adjacent wall tiles are not doors.\n" +
                "- If a door is shown in the ASCII map but is not listed in visibleDoors, ignore it.\n" +
                "- If a memory hint gives doorWorldX/doorWorldY, match it to a visibleDoor with the same worldX/worldY, then move toward that visibleDoor's row/col.\n" +
                "- Before choosing a movement toward a door, compare playerMapRow/playerMapCol with the selected visibleDoor row/col.\n" +
                "- If playerMapRow is smaller than door.row, use MoveDown.\n" +
                "- If playerMapRow is larger than door.row, use MoveUp.\n" +
                "- If playerMapCol is smaller than door.col, use MoveRight.\n" +
                "- If playerMapCol is larger than door.col, use MoveLeft.\n" +
                "- Only choose a movement if it reduces the distance to the selected visibleDoor exact row/col.\n" +
                "- If you are adjacent to D or Z, move directly onto the D or Z tile. Do not move beside it.\n" +
                "- When moving toward a visibleDoor, you may return multiple movement actions if they move through O tiles toward that door. The final action must be the one that steps onto D or Z, if included.\n\n";
        }

        string prompt =
            "You control a player in a grid-based dungeon crawler.\n" +
            "Return only valid JSON in this exact format:\n" +
            "{\"thought\":\"briefly explain your reasoning based on priority\",\"actions\":[\"MoveRight\",\"MoveRight\",\"MoveDown\"]}\n\n" +
            "{\"actions\":[\"MoveRight\",\"MoveRight\",\"MoveDown\"]}\n\n" +

            outputRulesSection +

            mapLegendSection +

            "Coordinate rules:\n" +
            "- Use playerMapRow/playerMapCol, visibleObjects, and visibleDoors for exact positions.\n" +
            "- For doors, visibleDoors is the source of truth. currentRoomMap is only visual context.\n" +
            "- Do not guess object or door positions from the ASCII map if visibleObjects or visibleDoors provides them.\n" +
            "- The first string in currentRoomMap is row 0, the top row.\n" +
            "- Moving Up decreases row by 1.\n" +
            "- Moving Down increases row by 1.\n" +
            "- Moving Left decreases col by 1.\n" +
            "- Moving Right increases col by 1.\n\n" +

            reflexionSection +

            "Goal:\n" +
            "- Goal: cut one tree if possible.\n\n" +

            localStrategySection +

            "Experiment method:\n" +
            methodInstruction + "\n" +

            memoryStrategySection +

            memoryRulesSection +

            ragRulesSection +

            "Anti-loop rules:\n" +
            "- Avoid repeating opposite movements such as MoveUp then MoveDown, or MoveLeft then MoveRight, unless it is clearly necessary.\n" +
            "- If recentActions show a loop, choose a different useful direction, usually toward a visible door, axe, tree, navigationHint, or unexploredDoorHint if those are available.\n\n" +

            doorRulesSection +

            "Current observation:\n" +
            observationJson + "\n\n" +

            "Known world memory:\n" +
            memoryJson;

        /*
        if (dungeonManager != null &&
        dungeonManager.experimentMethod == AIExperimentMethod.PromptMemoryGraphRag)
        {
            retrievedRulesJson = ragRetriever.GetRetrievedRulesJson(observationJson, memoryJson);
        }
        */

        string requestJson = BuildRequestJson(prompt);

        UnityWebRequest request = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        float requestStartTime = Time.realtimeSinceStartup;
        lastRequestSucceeded = false;
        lastApiLatencySeconds = 0f;

        yield return request.SendWebRequest();

        lastApiLatencySeconds = Time.realtimeSinceStartup - requestStartTime;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("OpenAI request failed: " + request.error);
            Debug.LogError(request.downloadHandler.text);
            yield break;
        }

        string responseText = request.downloadHandler.text;
        Debug.Log("OpenAI raw response: " + responseText);

        string actionsJson = ExtractOutputText(responseText);

        if (string.IsNullOrEmpty(actionsJson))
        {
            Debug.LogError("Could not extract actions JSON from response.");
            yield break;
        }

        File.WriteAllText(commandsFullPath, actionsJson);
        lastRequestSucceeded = true;
        Debug.Log("Saved AI commands to: " + commandsFullPath);
        Debug.Log(prompt);
    }

    private string BuildRequestJson(string prompt)
    {
        string escapedPrompt = JsonEscape(prompt);
        string tempString = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return
            "{" +
            "\"model\":\"" + model + "\"," +
            "\"temperature\":" + tempString + "," +
            "\"response_format\": {\"type\": \"json_object\"}," +
            "\"messages\": [{\"role\": \"user\", \"content\": \"" + escapedPrompt + "\"}]" +
            "}";
    }

    private string ExtractOutputText(string responseText)
    {
        OpenAIResponse response = JsonUtility.FromJson<OpenAIResponse>(responseText);

        if (response == null || response.choices == null || response.choices.Length == 0)
        {
            return "";
        }

        if (response.usage != null)
        {
            lastPromptTokens = response.usage.prompt_tokens;
            lastCompletionTokens = response.usage.completion_tokens;
            lastTotalTokens = response.usage.total_tokens;
        }

        string rawJson = response.choices[0].message.content;

        AIResponsePayload payload = JsonUtility.FromJson<AIResponsePayload>(rawJson);
        if (payload != null && payload.thought != null)
        {
            lastThought = payload.thought;
        }
        else
        {
            lastThought = "";
        }

        return rawJson;
    }

    [System.Serializable]
    public class OpenAIResponse
    {
        public OpenAIChoice[] choices;
        public OpenAIUsage usage;
    }

    [System.Serializable]
    public class OpenAIChoice
    {
        public OpenAIMessage message;
    }

    [System.Serializable]
    public class OpenAIMessage
    {
        public string content;
    }

    [System.Serializable]
    public class OpenAIUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    private string JsonEscape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

[System.Serializable]
public class OpenAIResponse
{
    public OpenAIOutput[] output;
}

[System.Serializable]
public class OpenAIOutput
{
    public OpenAIContent[] content;
}

[System.Serializable]
public class OpenAIContent
{
    public string type;
    public string text;
}

[System.Serializable]
public class AIResponsePayload
{
    public string thought;
}