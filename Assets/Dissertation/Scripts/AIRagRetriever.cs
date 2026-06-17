using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class AIRagRuleDatabase
{
    public List<AIRagRuleDocument> rules;
}

[System.Serializable]
public class AIRagRuleDocument
{
    public string id;
    public string title;
    public string text;
    public string[] tags;
}

[System.Serializable]
public class AIRagEmbeddingCache
{
    public string embeddingModel;
    public List<AIRagRuleEmbedding> entries;
}

[System.Serializable]
public class AIRagRuleEmbedding
{
    public string id;
    public float[] embedding;
}

[System.Serializable]
public class AIRagRetrievedRulesSnapshot
{
    public List<AIRagRetrievedRule> retrievedRules;
}

[System.Serializable]
public class AIRagRetrievedRule
{
    public string id;
    public string title;
    public float score;
    public string text;
}

[System.Serializable]
public class OpenAIEmbeddingResponse
{
    public OpenAIEmbeddingData[] data;
}

[System.Serializable]
public class OpenAIEmbeddingData
{
    public float[] embedding;
}

public class AIRagRetriever : MonoBehaviour
{
    public string apiKeyPath = "Dissertation/Secrets/openai_key.txt";
    public string rulesFilePath = "Dissertation/RAG/rules.json";
    public string cacheFilePath = "Dissertation/RAG/rule_embedding_cache.json";

    public string embeddingModel = "text-embedding-3-small";
    public int maxRetrievedRules = 3;
    public float minSimilarity = 0.25f;

    public string lastRetrievalQuery = "";
    public string lastRetrievedRuleIds = "";
    public string lastRetrievedRuleScores = "";
    public int lastRetrievedRuleCount = 0;
    public float lastRagLatencySeconds = 0f;
    public bool lastRetrievalSucceeded = false;

    private AIRagRuleDatabase ruleDatabase;
    private AIRagEmbeddingCache embeddingCache;

    public IEnumerator RetrieveRules(AIObservation observation, string memoryJson, Action<string> onComplete)
    {
        float startTime = Time.realtimeSinceStartup;

        lastRetrievalQuery = "";
        lastRetrievedRuleIds = "";
        lastRetrievedRuleScores = "";
        lastRetrievedRuleCount = 0;
        lastRagLatencySeconds = 0f;
        lastRetrievalSucceeded = false;

        if (onComplete == null)
        {
            yield break;
        }

        if (!LoadRuleDatabase())
        {
            onComplete("{}");
            yield break;
        }

        LoadEmbeddingCache();

        string apiKeyFullPath = Path.Combine(Application.dataPath, apiKeyPath);

        if (!File.Exists(apiKeyFullPath))
        {
            Debug.LogError("Embedding API key file not found: " + apiKeyFullPath);
            onComplete("{}");
            yield break;
        }

        string apiKey = File.ReadAllText(apiKeyFullPath).Trim();

        yield return StartCoroutine(EnsureRuleEmbeddings(apiKey));

        lastRetrievalQuery = BuildRetrievalQuery(observation, memoryJson);

        float[] queryEmbedding = null;
        yield return StartCoroutine(RequestEmbedding(apiKey, lastRetrievalQuery, result => queryEmbedding = result));

        if (queryEmbedding == null)
        {
            onComplete("{}");
            yield break;
        }

        List<ScoredRule> scoredRules = ScoreRules(queryEmbedding);
        scoredRules.Sort((a, b) => b.score.CompareTo(a.score));

        AIRagRetrievedRulesSnapshot snapshot = new AIRagRetrievedRulesSnapshot
        {
            retrievedRules = new List<AIRagRetrievedRule>()
        };

        List<string> ids = new List<string>();
        List<string> scores = new List<string>();

        for (int i = 0; i < scoredRules.Count && snapshot.retrievedRules.Count < maxRetrievedRules; i++)
        {
            if (scoredRules[i].score < minSimilarity)
            {
                continue;
            }

            snapshot.retrievedRules.Add(new AIRagRetrievedRule
            {
                id = scoredRules[i].rule.id,
                title = scoredRules[i].rule.title,
                score = scoredRules[i].score,
                text = scoredRules[i].rule.text
            });

            ids.Add(scoredRules[i].rule.id);
            scores.Add(scoredRules[i].score.ToString("F3", CultureInfo.InvariantCulture));
        }

        lastRetrievedRuleCount = snapshot.retrievedRules.Count;
        lastRetrievedRuleIds = string.Join("|", ids);
        lastRetrievedRuleScores = string.Join("|", scores);
        lastRagLatencySeconds = Time.realtimeSinceStartup - startTime;
        lastRetrievalSucceeded = true;

        string retrievedJson = JsonUtility.ToJson(snapshot, true);
        onComplete(retrievedJson);

        Debug.Log("RAG query: " + lastRetrievalQuery);
        Debug.Log("RAG retrieved count: " + lastRetrievedRuleCount);
        Debug.Log("RAG retrieved ids: " + lastRetrievedRuleIds);
        Debug.Log("RAG retrieved scores: " + lastRetrievedRuleScores);
        Debug.Log("RAG latency seconds: " + lastRagLatencySeconds.ToString("F3"));
        Debug.Log("RAG retrieved JSON: " + retrievedJson);
    }

    private bool LoadRuleDatabase()
    {
        string fullPath = Path.Combine(Application.dataPath, rulesFilePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogWarning("RAG rules file not found: " + fullPath);
            return false;
        }

        string json = File.ReadAllText(fullPath);
        ruleDatabase = JsonUtility.FromJson<AIRagRuleDatabase>(json);

        if (ruleDatabase == null || ruleDatabase.rules == null)
        {
            Debug.LogWarning("Invalid RAG rules database.");
            return false;
        }

        return true;
    }

    private void LoadEmbeddingCache()
    {
        string fullPath = Path.Combine(Application.dataPath, cacheFilePath);

        if (!File.Exists(fullPath))
        {
            embeddingCache = new AIRagEmbeddingCache
            {
                embeddingModel = embeddingModel,
                entries = new List<AIRagRuleEmbedding>()
            };

            return;
        }

        string json = File.ReadAllText(fullPath);
        embeddingCache = JsonUtility.FromJson<AIRagEmbeddingCache>(json);

        if (embeddingCache == null ||
            embeddingCache.entries == null ||
            embeddingCache.embeddingModel != embeddingModel)
        {
            embeddingCache = new AIRagEmbeddingCache
            {
                embeddingModel = embeddingModel,
                entries = new List<AIRagRuleEmbedding>()
            };
        }
    }

    private void SaveEmbeddingCache()
    {
        string fullPath = Path.Combine(Application.dataPath, cacheFilePath);
        string directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonUtility.ToJson(embeddingCache, true);
        File.WriteAllText(fullPath, json);
    }

    private IEnumerator EnsureRuleEmbeddings(string apiKey)
    {
        bool cacheChanged = false;

        foreach (AIRagRuleDocument rule in ruleDatabase.rules)
        {
            if (FindCachedEmbedding(rule.id) != null)
            {
                continue;
            }

            string embeddingText = rule.title + "\n" + rule.text;

            float[] embedding = null;
            yield return StartCoroutine(RequestEmbedding(apiKey, embeddingText, result => embedding = result));

            if (embedding == null)
            {
                continue;
            }

            embeddingCache.entries.Add(new AIRagRuleEmbedding
            {
                id = rule.id,
                embedding = embedding
            });

            cacheChanged = true;
        }

        if (cacheChanged)
        {
            SaveEmbeddingCache();
        }
    }

    private AIRagRuleEmbedding FindCachedEmbedding(string ruleId)
    {
        if (embeddingCache == null || embeddingCache.entries == null)
        {
            return null;
        }

        foreach (AIRagRuleEmbedding entry in embeddingCache.entries)
        {
            if (entry.id == ruleId)
            {
                return entry;
            }
        }

        return null;
    }

    private IEnumerator RequestEmbedding(string apiKey, string input, Action<float[]> onComplete)
    {
        string requestJson = BuildEmbeddingRequestJson(input);

        UnityWebRequest request = new UnityWebRequest("https://api.openai.com/v1/embeddings", "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestJson);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Embedding request failed: " + request.error);
            Debug.LogError(request.downloadHandler.text);
            onComplete(null);
            yield break;
        }

        OpenAIEmbeddingResponse response = JsonUtility.FromJson<OpenAIEmbeddingResponse>(request.downloadHandler.text);

        if (response == null ||
            response.data == null ||
            response.data.Length == 0 ||
            response.data[0].embedding == null)
        {
            Debug.LogError("Could not parse embedding response.");
            onComplete(null);
            yield break;
        }

        onComplete(response.data[0].embedding);
    }

    private string BuildEmbeddingRequestJson(string input)
    {
        return
            "{"
            + "\"model\":\"" + embeddingModel + "\","
            + "\"input\":\"" + JsonEscape(input) + "\""
            + "}";
    }

    private string BuildRetrievalQuery(AIObservation observation, string memoryJson)
    {
        List<string> parts = new List<string>();

        parts.Add("Goal: cut one tree if possible.");

        if (observation == null)
        {
            return string.Join(" ", parts);
        }

        parts.Add("Current room id: " + observation.currentRoomId);

        if (observation.inventory != null)
        {
            parts.Add("Inventory: " + string.Join(",", observation.inventory));
        }

        if (observation.visibleObjects != null && observation.visibleObjects.Count > 0)
        {
            List<string> objectTypes = new List<string>();

            foreach (AIMapObject obj in observation.visibleObjects)
            {
                objectTypes.Add(obj.type);
            }

            parts.Add("Visible objects: " + string.Join(",", objectTypes));

            if (objectTypes.Contains("cobweb"))
            {
                parts.Add("Cobweb hazard is visible. Cobweb movement and trap rules may be relevant.");
            }
        }

        if (observation.visibleDoors != null)
        {
            parts.Add("Visible doors: " + observation.visibleDoors.Count);
        }

        if (observation.currentRoomMap != null)
        {
            HashSet<char> symbols = new HashSet<char>();

            foreach (string row in observation.currentRoomMap)
            {
                if (string.IsNullOrEmpty(row))
                {
                    continue;
                }

                foreach (char symbol in row)
                {
                    symbols.Add(symbol);
                }
            }

            List<string> symbolStrings = new List<string>();

            foreach (char symbol in symbols)
            {
                symbolStrings.Add(symbol.ToString());
            }

            parts.Add("Visible map symbols: " + string.Join(",", symbolStrings));

            if (symbols.Contains('C'))
            {
                parts.Add("Cobweb hazard is visible in the current room. Cobweb trap and movement rules may be relevant.");
            }

            if (symbols.Contains('D') || symbols.Contains('Z'))
            {
                parts.Add("Door and corridor traversal rules may be relevant.");
            }

            if (symbols.Contains('T'))
            {
                parts.Add("Tree cutting and axe rules may be relevant.");
            }

            if (symbols.Contains('A'))
            {
                parts.Add("Axe pickup and inventory rules may be relevant.");
            }
        }

        if (!string.IsNullOrEmpty(memoryJson) && memoryJson != "{}")
        {
            parts.Add("Memory is available.");
        }

        return string.Join(" ", parts);
    }

    private List<ScoredRule> ScoreRules(float[] queryEmbedding)
    {
        List<ScoredRule> scored = new List<ScoredRule>();

        foreach (AIRagRuleDocument rule in ruleDatabase.rules)
        {
            AIRagRuleEmbedding cached = FindCachedEmbedding(rule.id);

            if (cached == null || cached.embedding == null)
            {
                continue;
            }

            scored.Add(new ScoredRule
            {
                rule = rule,
                score = CosineSimilarity(queryEmbedding, cached.embedding)
            });
        }

        return scored;
    }

    private float CosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return 0f;
        }

        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0.0 || normB == 0.0)
        {
            return 0f;
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    private string JsonEscape(string value)
    {
        if (value == null)
        {
            return "";
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private class ScoredRule
    {
        public AIRagRuleDocument rule;
        public float score;
    }
}