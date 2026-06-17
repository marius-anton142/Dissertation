using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class AIBaselineCommandDatabase
{
    public List<AIBaselineCommandEntry> entries;
}

[System.Serializable]
public class AIBaselineCommandEntry
{
    public string key;
    public List<string> actions;
}

public class AIBaselineRunner : MonoBehaviour
{
    public AICommandExecutor commandExecutor;
    public AITaskLogger taskLogger;
    public DungeonGenerationScript01 dungeonManager;
    public AIMemoryManager memoryManager;
    public AIObservationBuilder observationBuilder;

    public string baselineCommandsFilePath = "Dissertation/AI/baseline_commands.json";

    public KeyCode runBaselineKey = KeyCode.B;

    private bool isRunning = false;

    private string BuildBaselineKey()
    {
        return "seed" + dungeonManager.dungeonSeed +
               "_rooms" + dungeonManager.GetConfiguredRoomCount() +
               "_case" + dungeonManager.experimentCaseId;
    }

    private void Update()
    {
        if (Input.GetKeyDown(runBaselineKey) && !isRunning)
        {
            StartCoroutine(RunBaseline());
        }
    }

    private IEnumerator RunBaseline()
    {
        isRunning = true;

        AICommandList commandList = LoadBaselineCommandsForCurrentSetup();

        if (commandList == null || commandList.actions == null || commandList.actions.Count == 0)
        {
            Debug.LogWarning("No baseline commands available for current setup.");
            isRunning = false;
            yield break;
        }
        SaveCommandsToExecutorFile(commandList);

        int actionCount = commandList != null && commandList.actions != null
            ? commandList.actions.Count
            : 0;

        int initialTreeCount = dungeonManager.GetTreeCount();

        AIExperimentMethod oldMethod = dungeonManager.experimentMethod;
        string oldModelName = taskLogger.modelName;

        dungeonManager.experimentMethod = AIExperimentMethod.OracleCustom;
        taskLogger.modelName = "none";

        if (taskLogger != null)
        {
            taskLogger.StartTask(1, actionCount);
        }

        AIObservation observation = null;

        if (observationBuilder != null)
        {
            observation = observationBuilder.BuildObservation();

            if (memoryManager != null)
            {
                memoryManager.UpdateMemory(observation);
            }
        }

        int bumpsBefore = taskLogger != null && taskLogger.player != null
            ? taskLogger.player.aiBumpCount
            : 0;

        int doorsOpenedBefore = dungeonManager.aiDoorsOpenedCount;
        int corridorTraversalsBefore = commandExecutor.aiCorridorTraversalCount;

        yield return StartCoroutine(commandExecutor.ExecuteCommandsFromFile());

        if (observationBuilder != null && memoryManager != null)
        {
            AIObservation finalObservation = observationBuilder.BuildObservation();
            memoryManager.UpdateMemory(finalObservation);
        }

        int discoveredRoomsAfterStep = memoryManager != null ? memoryManager.GetDiscoveredRoomCount() : 0;

        int bumpsAfter = taskLogger != null && taskLogger.player != null
            ? taskLogger.player.aiBumpCount
            : 0;

        int doorsOpenedAfter = dungeonManager.aiDoorsOpenedCount;
        int corridorTraversalsAfter = commandExecutor.aiCorridorTraversalCount;

        int bumpsThisStep = bumpsAfter - bumpsBefore;
        int doorsOpenedThisStep = doorsOpenedAfter - doorsOpenedBefore;
        int corridorTraversalsThisStep = corridorTraversalsAfter - corridorTraversalsBefore;

        if (taskLogger != null && observation != null)
        {
            taskLogger.LogStep(
                0,
                observation,
                dungeonManager.GetTreeCount(),
                memoryManager != null ? memoryManager.GetKnownObjectCount() : 0,
                memoryManager != null ? memoryManager.GetNavigationHintCount(observation.currentRoomId) : 0,
                memoryManager != null ? memoryManager.GetUnexploredDoorHintCount(observation.currentRoomId) : 0,
                observation.visibleObjects != null ? observation.visibleObjects.Count : 0,
                observation.visibleDoors != null ? observation.visibleDoors.Count : 0,
                ActionsToLogString(commandList),
                actionCount,
                0f,
                true,
                bumpsThisStep,
                doorsOpenedThisStep,
                corridorTraversalsThisStep,
                memoryManager != null ? memoryManager.GetDiscoveredRoomCount() : 0,
                "",
                "",
                "",
                0,
                0f,
                false,
                0,
                0,
                0,
                "",
                0
            );
        }

        string failureReason = dungeonManager.GetTreeCount() < initialTreeCount
            ? "success"
            : "baseline_failed";

        if (taskLogger != null)
        {
            taskLogger.EndTask(
                1,
                actionCount,
                1,
                0,
                failureReason
            );
        }

        dungeonManager.experimentMethod = oldMethod;
        taskLogger.modelName = oldModelName;

        isRunning = false;
    }

    private AICommandList LoadBaselineCommandsForCurrentSetup()
    {
        string fullPath = Path.Combine(Application.dataPath, baselineCommandsFilePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogWarning("Baseline commands file not found: " + fullPath);
            return null;
        }

        string json = File.ReadAllText(fullPath);
        AIBaselineCommandDatabase database = JsonUtility.FromJson<AIBaselineCommandDatabase>(json);

        if (database == null || database.entries == null)
        {
            Debug.LogWarning("Invalid baseline command database.");
            return null;
        }

        string wantedKey = BuildBaselineKey();

        foreach (AIBaselineCommandEntry entry in database.entries)
        {
            if (entry.key == wantedKey)
            {
                return new AICommandList
                {
                    actions = entry.actions
                };
            }
        }

        Debug.LogWarning("No baseline commands found for key: " + wantedKey);
        return null;
    }

    private string ActionsToLogString(AICommandList commandList)
    {
        if (commandList == null || commandList.actions == null)
        {
            return "";
        }

        return string.Join("|", commandList.actions);
    }

    private void SaveCommandsToExecutorFile(AICommandList commandList)
    {
        string fullPath = Path.Combine(Application.dataPath, "Dissertation/AI/ai_commands.json");

        string directory = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonUtility.ToJson(commandList, true);
        File.WriteAllText(fullPath, json);
    }
}