using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class HumanDatasetCollector : MonoBehaviour
{
    public PlayerScript player;
    public AIObservationBuilder observationBuilder;
    public AIMemoryManager memoryManager;
    public DungeonGenerationScript01 dungeonManager;
    public WeaponDotScript weaponInventory;

    public string datasetFilePath = "Dissertation/AI/dataset.jsonl";
    public bool isRecording = false;

    public int maxRecentActions = 6;
    private List<string> recentActions = new List<string>();

    private bool wasBusyLastFrame = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            isRecording = !isRecording;
            Debug.Log("Dataset Recording: " + (isRecording ? "ON" : "OFF"));

            if (!isRecording)
            {
                recentActions.Clear();
            }
        }

        bool isBusyNow = player.IsBusy();
        bool wasBusy = wasBusyLastFrame;
        wasBusyLastFrame = isBusyNow;

        if (!isRecording || wasBusy) return;

        Vector3Int playerCell = GetPlayerCell();
        DungeonGenerationScript01.Room currentRoom = dungeonManager.GetRoomAtWorldCell(playerCell);

        if (currentRoom == null)
        {
            return;
        }

        string actionTaken = null;

        if (Input.GetKeyDown(KeyCode.W)) actionTaken = "MoveUp";
        else if (Input.GetKeyDown(KeyCode.S)) actionTaken = "MoveDown";
        else if (Input.GetKeyDown(KeyCode.A)) actionTaken = "MoveLeft";
        else if (Input.GetKeyDown(KeyCode.D)) actionTaken = "MoveRight";
        else if (Input.GetKeyDown(KeyCode.Alpha1)) actionTaken = "SelectWeaponSlot0";
        else if (Input.GetKeyDown(KeyCode.Alpha2)) actionTaken = "SelectWeaponSlot1";
        else if (Input.GetKeyDown(KeyCode.Alpha3)) actionTaken = "SelectWeaponSlot2";
        else if (Input.GetKeyDown(KeyCode.Alpha4)) actionTaken = "SelectWeaponSlot3";
        else if (Input.GetKeyDown(KeyCode.Q)) actionTaken = "DropSelectedWeapon";

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3 dir = mousePos - player.transform.position;

            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            {
                actionTaken = dir.x > 0 ? "UseWeaponRight" : "UseWeaponLeft";
            }
            else
            {
                actionTaken = dir.y > 0 ? "UseWeaponUp" : "UseWeaponDown";
            }
        }

        if (actionTaken != null)
        {
            RecordSample(actionTaken);
        }
    }

    private void RecordSample(string action)
    {
        AIObservation observation = observationBuilder.BuildObservation();

        observation.recentActions = new List<string>(recentActions);

        string memoryJson = "{}";

        if (memoryManager != null && dungeonManager.experimentMethod != AIExperimentMethod.PromptOnly)
        {
            memoryManager.UpdateMemory(observation);
            memoryJson = memoryManager.GetPromptMemoryJson(observation.currentRoomId);
            memoryJson = memoryJson.Replace("\n", "").Replace("\r", "").Replace("    ", "");
        }

        string observationJson = JsonUtility.ToJson(observation);
        string systemPrompt = "You control a player in a grid-based dungeon crawler. Output valid JSON in format {\"actions\":[\"ActionName\"]}.";
        string userPrompt = "Current observation:\n" + observationJson + "\n\nKnown world memory:\n" + memoryJson;
        string assistantResponse = "{\"actions\":[\"" + action + "\"]}";

        SaveToJsonl(systemPrompt, userPrompt, assistantResponse);

        recentActions.Add(action);
        if (recentActions.Count > maxRecentActions)
        {
            recentActions.RemoveAt(0);
        }
    }

    private void SaveToJsonl(string system, string user, string assistant)
    {
        string fullPath = Path.Combine(Application.dataPath, datasetFilePath);
        string directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string jsonlLine = $"{{\"messages\": [{{\"role\": \"system\", \"content\": \"{EscapeJson(system)}\"}}, {{\"role\": \"user\", \"content\": \"{EscapeJson(user)}\"}}, {{\"role\": \"assistant\", \"content\": \"{EscapeJson(assistant)}\"}}]}}";

        using (StreamWriter writer = new StreamWriter(fullPath, true))
        {
            writer.WriteLine(jsonlLine);
        }
    }

    private string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
    }

    private Vector3Int GetPlayerCell()
    {
        return new Vector3Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y),
            0
        );
    }
}