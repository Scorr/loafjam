using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class Game : MonoBehaviour
{
    [SerializeField] private List<GameObject> m_gameSystemPrefabs = new List<GameObject>();

    private static Game m_instance = null;

    private static Game GetInstance()
    {
        // if we didn't put a Game prefab in the scene just blow up
        // because prefab might have necessary systems that we don't create in code

        //if (m_instance == null && !IsDestroyed && Application.isPlaying)
        //    GameObjectExt.FindOrCreateGameObject("GameSystems").GetOrAddComponent<Game>();
        return m_instance;
    }

    private readonly List<IGameSystem> m_gameSystems = new List<IGameSystem>();
    private readonly Dictionary<Type, IGameSystem> m_gameSystemsByType = new Dictionary<Type, IGameSystem>();

    public static bool IsDestroyed { get; private set; }

    private void Awake()
    {
        IsDestroyed = false;

        if (m_instance != null)
        {
            Destroy(gameObject);
            Debug.LogError("More than one Game in scene.");
            return;
        }

        m_instance = this;
        DontDestroyOnLoad(gameObject);

        if (!CallbackDispatcher.IsInitialized)
        {
            gameObject.AddComponent<CallbackDispatcher>();
        }

        RegisterGameSystems();

        foreach (GameObject gameSystemPrefab in m_gameSystemPrefabs)
        {
            GameObject gameSystemGameObject = Instantiate(gameSystemPrefab, transform);
            IGameSystem gameSystem = gameSystemGameObject.GetComponent<IGameSystem>();
            Assert.IsNotNull(gameSystem);
            RegisterSystem(gameSystem);
        }

        foreach (IGameSystem gameSystem in m_gameSystems)
        {
            gameSystem.Setup();
        }
    }

    private void OnDestroy()
    {
        // Don't clean up if we're not the rightful heir to the castle.
        if (m_instance != this)
            return;

        IsDestroyed = true;

        // Cleanup subsystems
        foreach (IGameSystem subsystem in m_gameSystems)
        {
            subsystem.Dispose();
        }
    }

    private void RegisterGameSystems()
    {

    }

    private void RegisterSystem(IGameSystem gameSystem)
    {
        Type gameSystemType = gameSystem.GetType();

        m_gameSystems.Add(gameSystem);
        m_gameSystemsByType.Add(gameSystemType, gameSystem);
    }
    
    public static T GetSystem<T>() where T : class, IGameSystem
    {
        Assert.IsTrue(!IsDestroyed, "Cannot call GetSystem when Game is destroyed");
        GetInstance();
        Type subsystemType = typeof(T);

        IGameSystem gameSystem;
        if (m_instance.m_gameSystemsByType.TryGetValue(subsystemType, out gameSystem))
        {
            return (T) m_instance.m_gameSystemsByType[subsystemType];
        }

        Debug.LogError("[Game] GameSystem " + subsystemType + " has not been registered.");
        return null;
    }

    public static void BeginCoroutine(IEnumerator coroutine)
    {
        m_instance.StartCoroutine(coroutine);
    }
}