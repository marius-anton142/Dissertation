using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;

[System.Serializable]
public class AIResponse
{
    public string action;
    public List<string> actions;
}

public class AIClient : MonoBehaviour
{
    public AICommandExecutor commandExecutor;
    [HideInInspector] public bool isRequestFinished = true;

    private string serverUrl = "http://127.0.0.1:5000/get_action";
    public float lastApiLatencySeconds = 0f;
    public bool lastRequestSucceeded = false;
    public string lastActionsReturned = "";
    public int lastActionCount = 0;

    public void RequestAIDecision(string observation, string memory)
    {
        isRequestFinished = false;
        StartCoroutine(PostRequest(observation, memory));
    }

    IEnumerator PostRequest(string observation, string memory)
    {
        lastApiLatencySeconds = 0f;
        lastRequestSucceeded = false;
        lastActionsReturned = "";
        lastActionCount = 0;

        string json = "{\"observation\": " + observation + ", \"memory\": " + memory + "}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest www = new UnityWebRequest(serverUrl, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            float requestStartTime = Time.realtimeSinceStartup;

            yield return www.SendWebRequest();

            lastApiLatencySeconds = Time.realtimeSinceStartup - requestStartTime;

            if (www.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = www.downloadHandler.text;
                Debug.Log("Local AI raw response: " + jsonResponse);

                AIResponse response = JsonUtility.FromJson<AIResponse>(jsonResponse);
                List<string> actions = ExtractActions(response);

                lastActionsReturned = string.Join("|", actions);
                lastActionCount = actions.Count;
                lastRequestSucceeded = actions.Count > 0;

                if (actions.Count > 0)
                {
                    if (commandExecutor == null)
                    {
                        Debug.LogError("AIClient commandExecutor is not assigned.");
                    }
                    else
                    {
                        commandExecutor.RunAICommands(actions);
                    }
                }
                else
                {
                    Debug.LogWarning("Local AI returned no valid actions.");
                }
            }
            else
            {
                Debug.LogError("Local AI server error: " + www.error);
                Debug.LogError(www.downloadHandler.text);
            }
        }

        isRequestFinished = true;
    }

    private List<string> ExtractActions(AIResponse response)
    {
        List<string> actions = new List<string>();

        if (response == null)
        {
            return actions;
        }

        if (response.actions != null && response.actions.Count > 0)
        {
            actions.AddRange(response.actions);
            return actions;
        }

        if (!string.IsNullOrEmpty(response.action))
        {
            string trimmed = response.action.Trim();

            if (trimmed.StartsWith("{"))
            {
                AICommandList parsed = JsonUtility.FromJson<AICommandList>(trimmed);

                if (parsed != null && parsed.actions != null)
                {
                    actions.AddRange(parsed.actions);
                }
            }
            else
            {
                actions.Add(trimmed);
            }
        }

        return actions;
    }
}