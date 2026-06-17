using System.Collections;
using UnityEngine;
using System.IO;

public class AIReplanningController : MonoBehaviour
{
    public AIObservationBuilder observationBuilder;
    public AIPlannerClient plannerClient;
    public AICommandExecutor commandExecutor;
    public AITaskLogger taskLogger;
    public DungeonGenerationScript01 dungeonManager;
    public AIMemoryManager memoryManager;

    public int maxSteps = 30;
    public float delayBetweenReplans = 0.1f;

    private bool isRunning = false;
    private int initialTreeCount;
    public int maxActionsPerPlan = 4;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G) && !isRunning)
        {
            StartCoroutine(RunReplanningLoop());
        }

        if (Input.GetKeyDown(KeyCode.M))
        {
            DebugUpdateMemoryOnly();
        }

        if (Input.GetKeyDown(KeyCode.L) && !isRunning)
        {
            Debug.Log("L-Key: Starting Local AI Loop");
            StartCoroutine(RunLocalAILoop());
        }
    }

    private IEnumerator RunLocalAILoop()
    {
        isRunning = true;

        initialTreeCount = dungeonManager.GetTreeCount();

        int stepsTaken = 0;
        int apiCalls = 0;
        string failureReason = "max_steps_reached";

        AIClient localClient = GetComponent<AIClient>();

        if (localClient == null)
        {
            Debug.LogError("AIClient component not found on the same GameObject.");
            isRunning = false;
            yield break;
        }

        if (taskLogger != null)
        {
            taskLogger.StartTask(
                maxSteps,
                maxActionsPerPlan,
                "local_bc",
                "Llama-3.1-8B-LoRA"
            );
        }

        for (int step = 0; step < maxSteps; step++)
        {
            if (dungeonManager.GetTreeCount() < initialTreeCount)
            {
                Debug.Log("Local task completed after " + step + " steps.");
                failureReason = "success";
                break;
            }

            AIObservation observation = observationBuilder.BuildObservation();

            if (memoryManager != null)
            {
                memoryManager.UpdateMemory(observation);
            }

            int bumpsBefore = taskLogger != null && taskLogger.player != null
                ? taskLogger.player.aiBumpCount
                : 0;

            int doorsOpenedBefore = dungeonManager.aiDoorsOpenedCount;
            int corridorTraversalsBefore = commandExecutor.aiCorridorTraversalCount;

            string obsJson = JsonUtility.ToJson(observation);
            string memoryJson = memoryManager != null
                ? memoryManager.GetMemoryJson(observation.currentRoomId)
                : "{}";

            apiCalls++;

            localClient.RequestAIDecision(obsJson, memoryJson);

            yield return new WaitUntil(() => localClient.isRequestFinished);
            yield return new WaitUntil(() => !commandExecutor.isExecuting);

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
                    step,
                    observation,
                    dungeonManager.GetTreeCount(),
                    memoryManager != null ? memoryManager.GetKnownObjectCount() : 0,
                    0,
                    0,
                    observation.visibleObjects != null ? observation.visibleObjects.Count : 0,
                    observation.visibleDoors != null ? observation.visibleDoors.Count : 0,
                    localClient.lastActionsReturned,
                    localClient.lastActionCount,
                    localClient.lastApiLatencySeconds,
                    localClient.lastRequestSucceeded,
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

            if (!localClient.lastRequestSucceeded)
            {
                failureReason = "local_api_error";
                break;
            }

            stepsTaken++;

            yield return new WaitForSeconds(delayBetweenReplans);
        }

        if (dungeonManager.GetTreeCount() < initialTreeCount)
        {
            failureReason = "success";
        }

        if (taskLogger != null)
        {
            taskLogger.EndTask(
                maxSteps,
                maxActionsPerPlan,
                stepsTaken,
                apiCalls,
                failureReason
            );
        }

        isRunning = false;

        Debug.Log("Local AI loop finished.");
    }

    private IEnumerator RunReplanningLoop()
    {
        isRunning = true;

        initialTreeCount = dungeonManager.GetTreeCount();

        int stepsTaken = 0;
        int apiCalls = 0;
        string failureReason = "max_steps_reached";

        if (taskLogger != null)
        {
            taskLogger.StartTask(maxSteps, maxActionsPerPlan);
        }

        for (int step = 0; step < maxSteps; step++)
        {
            if (dungeonManager.GetTreeCount() < initialTreeCount)
            {
                Debug.Log("Task completed after " + step + " steps.");
                failureReason = "success";
                break;
            }

            AIObservation observation = observationBuilder.BuildObservation();

            if (memoryManager != null)
            {
                memoryManager.UpdateMemory(observation);
            }

            observationBuilder.SaveObservationToFile();

            int bumpsBefore = taskLogger != null && taskLogger.player != null
                ? taskLogger.player.aiBumpCount
                : 0;

            int doorsOpenedBefore = dungeonManager.aiDoorsOpenedCount;
            int corridorTraversalsBefore = commandExecutor.aiCorridorTraversalCount;

            apiCalls++;
            yield return StartCoroutine(plannerClient.RequestPlan());

            if (plannerClient == null || !plannerClient.lastRequestSucceeded)
            {
                Debug.LogWarning("Skipping command execution because planner request failed.");
                failureReason = "api_error";
                break;
            }

            AICommandList returnedCommands = LoadLastCommandsForLogging();
            string actionsReturned = ActionsToLogString(returnedCommands);
            int actionCount = GetActionCount(returnedCommands);

            yield return StartCoroutine(commandExecutor.ExecuteCommandsFromFile());

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
                    step,
                    observation,
                    dungeonManager.GetTreeCount(),
                    memoryManager != null ? memoryManager.GetKnownObjectCount() : 0,
                    memoryManager != null ? memoryManager.GetNavigationHintCount(observation.currentRoomId) : 0,
                    memoryManager != null ? memoryManager.GetUnexploredDoorHintCount(observation.currentRoomId) : 0,
                    observation.visibleObjects != null ? observation.visibleObjects.Count : 0,
                    observation.visibleDoors != null ? observation.visibleDoors.Count : 0,
                    actionsReturned,
                    actionCount,
                    plannerClient != null ? plannerClient.lastApiLatencySeconds : 0f,
                    plannerClient != null && plannerClient.lastRequestSucceeded,
                    bumpsThisStep,
                    doorsOpenedThisStep,
                    corridorTraversalsThisStep,
                    memoryManager != null ? memoryManager.GetDiscoveredRoomCount() : 0,
                    plannerClient != null && plannerClient.ragRetriever != null ? plannerClient.ragRetriever.lastRetrievalQuery : "",
                    plannerClient != null && plannerClient.ragRetriever != null ? plannerClient.ragRetriever.lastRetrievedRuleIds : "",
                    plannerClient != null && plannerClient.ragRetriever != null ? plannerClient.ragRetriever.lastRetrievedRuleScores : "",
                    plannerClient != null && plannerClient.ragRetriever != null ? plannerClient.ragRetriever.lastRetrievedRuleCount : 0,
                    plannerClient != null && plannerClient.ragRetriever != null ? plannerClient.ragRetriever.lastRagLatencySeconds : 0f,
                    plannerClient != null && plannerClient.ragRetriever != null && plannerClient.ragRetriever.lastRetrievalSucceeded,
                    plannerClient != null ? plannerClient.lastPromptTokens : 0,
                    plannerClient != null ? plannerClient.lastCompletionTokens : 0,
                    plannerClient != null ? plannerClient.lastTotalTokens : 0,
                    plannerClient != null ? plannerClient.lastThought : "",
                    plannerClient != null ? plannerClient.stuckCounter : 0
                );
            }

            stepsTaken++;

            yield return new WaitForSeconds(delayBetweenReplans);
        }

        if (dungeonManager.GetTreeCount() < initialTreeCount)
        {
            failureReason = "success";
        }

        if (taskLogger != null)
        {
            taskLogger.EndTask(
                maxSteps,
                maxActionsPerPlan,
                stepsTaken,
                apiCalls,
                failureReason
            );
        }

        isRunning = false;
    }

    private void DebugUpdateMemoryOnly()
    {
        Debug.Log("M pressed");

        if (observationBuilder == null)
        {
            Debug.LogWarning("ObservationBuilder is not assigned.");
            return;
        }

        if (memoryManager == null)
        {
            Debug.LogWarning("MemoryManager is not assigned.");
            return;
        }

        AIObservation observation = observationBuilder.BuildObservation();

        if (observation == null)
        {
            Debug.LogWarning("Observation is null.");
            return;
        }

        Debug.Log("Current room id: " + observation.currentRoomId);

        memoryManager.UpdateMemory(observation);

        string memoryJson = memoryManager.GetMemoryJson(observation.currentRoomId);

        Debug.Log("Memory JSON:");
        Debug.Log(memoryJson);
    }

    private AICommandList LoadLastCommandsForLogging()
    {
        string fullPath = Path.Combine(Application.dataPath, "Dissertation/AI/ai_commands.json");

        if (!File.Exists(fullPath))
        {
            return null;
        }

        string json = File.ReadAllText(fullPath);
        return JsonUtility.FromJson<AICommandList>(json);
    }

    private string ActionsToLogString(AICommandList commandList)
    {
        if (commandList == null || commandList.actions == null)
        {
            return "";
        }

        return string.Join("|", commandList.actions);
    }

    private int GetActionCount(AICommandList commandList)
    {
        if (commandList == null || commandList.actions == null)
        {
            return 0;
        }

        return commandList.actions.Count;
    }
}