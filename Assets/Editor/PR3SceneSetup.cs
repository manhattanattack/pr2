// Одна команда создаёт все элементы из ПР3 в существующих префабах и GameScene.
// Запускать в Unity: Tools > PR3 > Собрать боевую сцену.
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PR3SceneSetup
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string NetworkPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
    private const string LocalPrefabPath = "Assets/Prefabs/LocalPlayer.prefab";
    private const string ProjectilePrefabPath = "Assets/Prefabs/Projectile_Visual.prefab";
    private const string ProjectileMaterialPath = "Assets/Materials/Mat_Projectile.mat";

    [MenuItem("Tools/PR3/Собрать боевую сцену")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PR3] Выйдите из Play Mode перед сборкой.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        try
        {
            Material projectileMaterial = EnsureProjectileMaterial();
            GameObject projectilePrefab = EnsureProjectilePrefab(projectileMaterial);
            ConfigureNetworkPlayer();
            ConfigureLocalPlayer();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject hudObject = GameObject.Find("GameHUD");
            GameObject managerObject = GameObject.Find("[NETWORK_MANAGER]");
            if (hudObject == null || managerObject == null)
                throw new Exception("В GameScene не найден GameHUD или [NETWORK_MANAGER].");

            CombatHUD hud = ConfigureHUD(hudObject);
            DamageFlash damage = hudObject.GetComponent<DamageFlash>();
            if (damage == null) damage = hudObject.AddComponent<DamageFlash>();

            Image damageImage = EnsurePanel(hudObject.transform, "DamageVignette", new Color(1f, 0f, 0f, 0f));
            damageImage.raycastTarget = false;
            SetReference(damage, "flashImage", damageImage);

            // Затем поднимаем DeathPanel поверх красной вспышки.
            Transform deathPanel = hudObject.transform.Find("DeathPanel");
            if (deathPanel != null) deathPanel.SetAsLastSibling();

            NetworkClient client = managerObject.GetComponent<NetworkClient>();
            if (client == null) throw new Exception("У [NETWORK_MANAGER] отсутствует NetworkClient.");
            ProjectileManager manager = managerObject.GetComponent<ProjectileManager>();
            if (manager == null) manager = managerObject.AddComponent<ProjectileManager>();
            SetReference(manager, "visualProjectilePrefab", projectilePrefab);
            SetReference(client, "combatHUD", hud);
            SetReference(client, "damageFlash", damage);
            SetReference(client, "projectileManager", manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PR3] Сборка завершена: префабы, оружие, интерфейс HP, экран смерти, снаряд и сетевые ссылки готовы.");
            EditorUtility.DisplayDialog("ПР3", "Сцена и префабы собраны! Проверьте Console и запускайте сервер/игру.", "ОК");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("ПР3: ошибка", ex.Message, "ОК");
        }
    }

    private static Material EnsureProjectileMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ProjectileMaterialPath);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) throw new Exception("Не найден подходящий Shader для снаряда.");

        mat = new Material(shader) { name = "Mat_Projectile" };
        Color glow = new Color(1f, 0.38f, 0.05f, 1f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", glow);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", glow);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", glow * 2f);
        }
        AssetDatabase.CreateAsset(mat, ProjectileMaterialPath);
        return mat;
    }

    private static GameObject EnsureProjectilePrefab(Material mat)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
        if (prefab != null) return prefab;

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Projectile_Visual";
        sphere.transform.localScale = Vector3.one * 0.3f;
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = mat;
        TrailRenderer trail = sphere.AddComponent<TrailRenderer>();
        trail.time = 0.15f;
        trail.widthCurve = AnimationCurve.Linear(0f, 0.2f, 1f, 0f);
        trail.materials = new[] { mat };
        sphere.AddComponent<VisualProjectile>();

        prefab = PrefabUtility.SaveAsPrefabAsset(sphere, ProjectilePrefabPath);
        UnityEngine.Object.DestroyImmediate(sphere);
        if (prefab == null) throw new Exception("Не удалось создать Projectile_Visual.");
        return prefab;
    }

    private static void ConfigureNetworkPlayer()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(NetworkPrefabPath);
        try
        {
            Transform canvas = FindTransform(root.transform, "NameCanvas");
            if (canvas == null) throw new Exception("NetworkPlayer: не найден NameCanvas.");
            RectTransform rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 70f);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 2.4f);

            Transform nickname = FindTransform(canvas, "Text (TMP)");
            if (nickname != null)
            {
                RectTransform nicknameRect = nickname.GetComponent<RectTransform>();
                nicknameRect.anchoredPosition = new Vector2(0f, 17f);
                nicknameRect.sizeDelta = new Vector2(200f, 30f);
            }

            Slider slider = EnsureSlider(canvas, "HealthBar", new Vector2(160f, 14f),
                new Vector2(0f, -15f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Color(0.12f, 0.12f, 0.12f, 0.9f), new Color(0.18f, 0.86f, 0.25f, 1f));

            NetworkPlayer player = root.GetComponent<NetworkPlayer>();
            if (player == null) throw new Exception("NetworkPlayer: не найден скрипт NetworkPlayer.");
            SetReference(player, "healthSlider", slider);
            SetReference(player, "nameCanvasObject", canvas.gameObject);

            NetworkPlayerVisualFeedback feedback = root.GetComponent<NetworkPlayerVisualFeedback>();
            if (feedback == null) feedback = root.AddComponent<NetworkPlayerVisualFeedback>();
            Transform body = FindTransform(root.transform, "BodyMesh");
            if (body == null) throw new Exception("NetworkPlayer: не найден BodyMesh.");
            SetReference(feedback, "bodyRenderer", body.GetComponent<Renderer>());
            SetReference(player, "visualFeedback", feedback);

            PrefabUtility.SaveAsPrefabAsset(root, NetworkPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ConfigureLocalPlayer()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LocalPrefabPath);
        try
        {
            Transform head = FindTransform(root.transform, "HeadPivot");
            if (head == null) throw new Exception("LocalPlayer: не найден HeadPivot.");
            Transform holder = head.Find("WeaponHolder");
            if (holder == null)
            {
                GameObject go = new GameObject("WeaponHolder");
                go.transform.SetParent(head, false);
                holder = go.transform;
            }
            holder.localPosition = new Vector3(0.35f, -0.25f, 0.5f);
            holder.localRotation = Quaternion.identity;

            Transform weapon = holder.Find("MeleeWeapon");
            if (weapon == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "MeleeWeapon";
                cube.transform.SetParent(holder, false);
                Collider collider = cube.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                weapon = cube.transform;
            }
            weapon.localPosition = new Vector3(0f, 0.15f, 0f);
            weapon.localRotation = Quaternion.Euler(45f, 0f, 0f);
            weapon.localScale = new Vector3(0.08f, 0.4f, 0.08f);

            MeleeAnimator animator = root.GetComponent<MeleeAnimator>();
            if (animator == null) animator = root.AddComponent<MeleeAnimator>();
            SetReference(animator, "weaponHolder", holder);
            LocalPlayerController controller = root.GetComponent<LocalPlayerController>();
            if (controller == null) throw new Exception("LocalPlayer: отсутствует LocalPlayerController.");
            SetReference(controller, "meleeAnimator", animator);

            PrefabUtility.SaveAsPrefabAsset(root, LocalPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static CombatHUD ConfigureHUD(GameObject canvas)
    {
        Slider hp = EnsureSlider(canvas.transform, "PlayerHealthBar", new Vector2(260f, 24f),
            new Vector2(40f, 40f), Vector2.zero, Vector2.zero,
            new Color(0.13f, 0.13f, 0.13f, 0.9f), new Color(0.2f, 0.9f, 0.2f, 1f));
        hp.GetComponent<RectTransform>().pivot = Vector2.zero;

        TextMeshProUGUI hpText = EnsureText(hp.transform, "Text_HP", "100 / 100", 16f, Color.white,
            Vector2.zero, new Vector2(250f, 24f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);

        TextMeshProUGUI score = EnsureText(canvas.transform, "Text_Score", "Побед: 0", 26f, Color.yellow,
            new Vector2(40f, -40f), new Vector2(200f, 50f), new Vector2(0f, 1f), TextAlignmentOptions.TopLeft);
        score.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        score.fontStyle = FontStyles.Bold;

        Image panelImage = EnsurePanel(canvas.transform, "DeathPanel", new Color(0.16f, 0f, 0f, 0.75f));
        GameObject panel = panelImage.gameObject;
        TextMeshProUGUI title = EnsureText(panel.transform, "Text_Death", "ВЫ ПОГИБЛИ", 48f, Color.red,
            new Vector2(0f, 35f), new Vector2(650f, 90f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
        TextMeshProUGUI timer = EnsureText(panel.transform, "Text_RespawnTimer", "Возрождение через: 3...",
            24f, Color.white, new Vector2(0f, -45f), new Vector2(650f, 55f),
            new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);

        CombatHUD combatHUD = canvas.GetComponent<CombatHUD>();
        if (combatHUD == null) combatHUD = canvas.AddComponent<CombatHUD>();
        SetReference(combatHUD, "localHealthSlider", hp);
        SetReference(combatHUD, "localHealthText", hpText);
        SetReference(combatHUD, "scoreText", score);
        SetReference(combatHUD, "deathPanel", panel);
        SetReference(combatHUD, "respawnTimerText", timer);
        panel.SetActive(false);
        return combatHUD;
    }

    private static Slider EnsureSlider(Transform parent, string name, Vector2 size, Vector2 pos,
        Vector2 anchorMin, Vector2 anchorMax, Color backColor, Color fillColor)
    {
        Transform existing = parent.Find(name);
        if (existing != null && existing.GetComponent<Slider>() != null)
            return existing.GetComponent<Slider>();

        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;

        Image background = EnsureImage(go.transform, "Background", backColor);
        Stretch(background.rectTransform);
        Image fill = EnsureImage(go.transform, "Fill Area/Fill", fillColor);
        Stretch(fill.rectTransform);

        Slider slider = go.GetComponent<Slider>();
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;
        slider.targetGraphic = background;
        slider.fillRect = fill.rectTransform;
        slider.minValue = 0;
        slider.maxValue = 100;
        slider.value = 100;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Image EnsureImage(Transform parent, string path, Color color)
    {
        Transform current = parent;
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            Transform found = current.Find(parts[i]);
            if (found == null)
            {
                GameObject go = new GameObject(parts[i], typeof(RectTransform));
                go.transform.SetParent(current, false);
                found = go.transform;
                Stretch((RectTransform)found);
            }
            current = found;
        }
        Image image = current.GetComponent<Image>();
        if (image == null) image = current.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image EnsurePanel(Transform parent, string name, Color color)
    {
        Image image = EnsureImage(parent, name, color);
        Stretch(image.rectTransform);
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI EnsureText(Transform parent, string name, string content,
        float fontSize, Color color, Vector2 position, Vector2 size, Vector2 anchor, TextAlignmentOptions alignment)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing == null)
        {
            go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
        }
        else go = existing.gameObject;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        if (text == null) text = go.AddComponent<TextMeshProUGUI>();
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindTransform(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindTransform(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null) throw new Exception($"Не найдено поле {field} в {target.name}");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
