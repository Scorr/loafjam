using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

public enum CallbackType
{
    UPDATE,
    FIXED_UPDATE,
    LATE_UPDATE,
    ON_GUI
}

/// <summary>
/// Used to prevent garbage from boxing when comparing enums.
/// </summary>
public struct CallbackTypeComparer : IEqualityComparer<CallbackType>
{
    public bool Equals(CallbackType x, CallbackType y)
    {
        return x == y;
    }

    public int GetHashCode(CallbackType obj)
    {
        return (int)obj;
    }
}

/// <summary>
/// Circumvents overhead of Unity callbacks such as Update().
/// </summary>
public class CallbackDispatcher : MonoBehaviour
{
    private static CallbackDispatcher instance;

    public class NamedFunc
    {
        // Func instead of Action so that the callback can be stopped when false is returned.
        public Func<bool> Func;
        public string Name;
        public bool WaitingForRemove;
    }

    public class CallbackCollection
    {
        public readonly List<NamedFunc> FunctionsList = new List<NamedFunc>(64);
        public readonly Dictionary<Func<bool>, NamedFunc> FunctionsDictionary = new Dictionary<Func<bool>, NamedFunc>(64);
    }

    public static Dictionary<CallbackType, CallbackCollection> collections;

    public static bool IsInitialized { get { return instance != null; } }

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        collections = new Dictionary<CallbackType, CallbackCollection>(2, new CallbackTypeComparer());
        foreach (CallbackType callbackType in Enum.GetValues(typeof(CallbackType)))
        {
            if (!collections.ContainsKey(callbackType))
            {
                collections.Add(callbackType, new CallbackCollection());
            }
        }
    }

    private void Update()
    {
        ProcessCallbacks(collections[CallbackType.UPDATE]);
    }

    private void LateUpdate()
    {
        ProcessCallbacks(collections[CallbackType.LATE_UPDATE]);
    }

    private void FixedUpdate()
    {
        ProcessCallbacks(collections[CallbackType.FIXED_UPDATE]);
    }

    // PHASED_DEBUG only to avoid overhead in release
#if PHASED_DEBUG
    private void OnGUI()
    {
        ProcessCallbacks(collections[CallbackType.ON_GUI]);
    }
#endif

    private void OnDestroy()
    {
        instance = null;
    }

    private static void RemoveCallbackInternal(Dictionary<CallbackType, CallbackCollection> collection, CallbackType type, Func<bool> callback)
    {
        CallbackCollection callbackCollection;
        if (collection.TryGetValue(type, out callbackCollection))
        {
            RemoveCallbackInternal(callbackCollection, callback);
        }
    }

    private static void RemoveCallbackInternal(CallbackCollection callbackCollection, Func<bool> callback)
    {
        NamedFunc namedFunc;
        if (callbackCollection.FunctionsDictionary.TryGetValue(callback, out namedFunc))
        {
            namedFunc.WaitingForRemove = true;
            callbackCollection.FunctionsDictionary.Remove(callback);
        }
    }

    private void ProcessCallbacks(CallbackCollection collection)
    {
        List<NamedFunc> callbackList = collection.FunctionsList;

        int startCount = callbackList.Count;
        int count = startCount;
        for (int i = count - 1; i >= 0; --i)
        {
            NamedFunc namedFunc = callbackList[i];
            Func<bool> callback = namedFunc.Func;

            if (namedFunc.WaitingForRemove)
            {
                NamedFunc last = callbackList[count - 1];
                callbackList[count - 1] = namedFunc;
                callbackList[i] = last;
                count--;
                continue;
            }

            Profiler.BeginSample(namedFunc.Name);
            try
            {
                if (!callback())
                {
                    RemoveCallbackInternal(collection, callback);
                }
            }
            catch (Exception e)
            {
                // Catch the exception so it does not break flow of all callbacks
                // But still log it to Unity console so we know something happened
                Debug.LogException(e);
            }
            Profiler.EndSample();
        }

        callbackList.RemoveRange(count, startCount - count);
    }

    private static void AddCallbackInternal(Dictionary<CallbackType, CallbackCollection> collection, CallbackType type, Func<bool> callback)
    {
        if (!collection.ContainsKey(type))
        {
            collection.Add(type, new CallbackCollection());
        }

        NamedFunc namedFunc = new NamedFunc();
        // We do PHASED_DEBUG instead of UNITY_EDITOR in case we connect profiler in debug build
#if PHASED_DEBUG
        namedFunc.Name = callback.Target != null ?
            callback.Target.GetType().ToString() + "." + callback.Method.ToString() :
            callback.Method.ToString();
#endif
        namedFunc.Func = callback;

        CallbackCollection callbackCollection = collection[type];

        if (callbackCollection.FunctionsDictionary.ContainsKey(callback))
        {
            Debug.LogErrorFormat("Failed to add callback '{0}' to CallbackEvent '{1}' because it is already added.", namedFunc.Name, type.ToString());
            return;
        }

        callbackCollection.FunctionsList.Add(namedFunc);
        callbackCollection.FunctionsDictionary.Add(callback, namedFunc);
    }

    /// <summary>
    /// Add a callback to the dispatcher system.
    /// Callbacks will continue to be called until they return false.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="callback">Callback to call.</param>
    public static void AddCallback(CallbackType type, Func<bool> callback)
    {
        AddCallbackInternal(collections, type, callback);
    }

    public static void RemoveCallback(CallbackType type, Func<bool> callback)
    {
        RemoveCallbackInternal(collections, type, callback);
    }
    
    // Seperate method to allow conditional
    [Conditional("PHASED_DEBUG")]
    public static void AddOnGUICallback(Func<bool> callback)
    {
        AddCallbackInternal(collections, CallbackType.ON_GUI, callback);
    }

    // Seperate method to allow conditional
    [Conditional("PHASED_DEBUG")]
    public static void RemoveOnGUICallback(Func<bool> callback)
    {
        RemoveCallbackInternal(collections, CallbackType.ON_GUI, callback);
    }
}
