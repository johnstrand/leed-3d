using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Viewport;
using Raylib_cs;

namespace Leed3D.Editor.Viewport;

/// <summary>
/// Manages the embedded GLSL 330 forward multi-light and distance fog shader
/// for real-time viewport lit and textured preview mode.
/// </summary>
public sealed class LitPreviewShader : IDisposable
{
    public const int MaxLights = SceneLightGatherer.MaxLights;

    private Shader _shader;
    private bool _isLoaded;
    private bool _isDisposed;

    private int _uViewPosLoc = -1;
    private int _uNumLightsLoc = -1;
    private int _uAmbientColorLoc = -1;
    private int _uAmbientIntensityLoc = -1;
    private int _uFogEnabledLoc = -1;
    private int _uFogColorLoc = -1;
    private int _uFogDensityLoc = -1;

    private readonly LightUniformLocations[] _lightLocs = new LightUniformLocations[MaxLights];

    private struct LightUniformLocations
    {
        public int TypeLoc;
        public int PosLoc;
        public int DirLoc;
        public int ColorLoc;
        public int IntensityLoc;
        public int RadiusLoc;
        public int SpotCosCutoffLoc;
    }

    public bool IsReady => _isLoaded && _shader.Id != 0;
    public Shader Shader => _shader;

    public void EnsureLoaded()
    {
        if (_isLoaded || !Raylib.IsWindowReady())
        {
            return;
        }

        try
        {
            _shader = Raylib.LoadShaderFromMemory(VertexShaderSource, FragmentShaderSource);
            if (_shader.Id != 0)
            {
                CacheUniformLocations();
                _isLoaded = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LitPreviewShader] Failed to compile/load GLSL 330 lit shader: {ex.Message}");
        }
    }

    private void CacheUniformLocations()
    {
        _uViewPosLoc = Raylib.GetShaderLocation(_shader, "uViewPos");
        _uNumLightsLoc = Raylib.GetShaderLocation(_shader, "uNumLights");
        _uAmbientColorLoc = Raylib.GetShaderLocation(_shader, "uAmbientColor");
        _uAmbientIntensityLoc = Raylib.GetShaderLocation(_shader, "uAmbientIntensity");
        _uFogEnabledLoc = Raylib.GetShaderLocation(_shader, "uFogEnabled");
        _uFogColorLoc = Raylib.GetShaderLocation(_shader, "uFogColor");
        _uFogDensityLoc = Raylib.GetShaderLocation(_shader, "uFogDensity");

        for (int i = 0; i < MaxLights; i++)
        {
            _lightLocs[i] = new LightUniformLocations
            {
                TypeLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].type"),
                PosLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].position"),
                DirLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].direction"),
                ColorLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].color"),
                IntensityLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].intensity"),
                RadiusLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].radius"),
                SpotCosCutoffLoc = Raylib.GetShaderLocation(_shader, $"lights[{i}].spotCosCutoff")
            };
        }
    }

    /// <summary>
    /// Binds the lit preview shader and updates multi-light, camera, and fog uniforms.
    /// </summary>
    public void Begin(EditorCamera camera, IReadOnlyList<SceneLightData> lights, EnvironmentLightingSettings env)
    {
        EnsureLoaded();

        if (!IsReady)
        {
            return;
        }

        Vector3 camPosRh = CoordSpace.ToRightHanded(camera.Position);
        if (_uViewPosLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uViewPosLoc, camPosRh, ShaderUniformDataType.Vec3);
        }

        int activeLightCount = Math.Min(lights.Count, MaxLights);
        if (_uNumLightsLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uNumLightsLoc, activeLightCount, ShaderUniformDataType.Int);
        }

        if (_uAmbientColorLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uAmbientColorLoc, env.AmbientColorNormalized, ShaderUniformDataType.Vec3);
        }
        if (_uAmbientIntensityLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uAmbientIntensityLoc, env.AmbientIntensity, ShaderUniformDataType.Float);
        }

        if (_uFogEnabledLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uFogEnabledLoc, env.FogEnabled ? 1 : 0, ShaderUniformDataType.Int);
        }
        if (_uFogColorLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uFogColorLoc, env.FogColorNormalized, ShaderUniformDataType.Vec3);
        }
        if (_uFogDensityLoc != -1)
        {
            Raylib.SetShaderValue(_shader, _uFogDensityLoc, env.FogDensity, ShaderUniformDataType.Float);
        }

        // Upload per-light uniforms
        for (int i = 0; i < activeLightCount; i++)
        {
            SceneLightData light = lights[i];
            ref LightUniformLocations loc = ref _lightLocs[i];

            if (loc.TypeLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.TypeLoc, (int)light.Type, ShaderUniformDataType.Int);
            }
            if (loc.PosLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.PosLoc, light.PositionRightHanded, ShaderUniformDataType.Vec3);
            }
            if (loc.DirLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.DirLoc, light.DirectionRightHanded, ShaderUniformDataType.Vec3);
            }
            if (loc.ColorLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.ColorLoc, light.ColorNormalized, ShaderUniformDataType.Vec4);
            }
            if (loc.IntensityLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.IntensityLoc, light.Intensity, ShaderUniformDataType.Float);
            }
            if (loc.RadiusLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.RadiusLoc, light.Radius, ShaderUniformDataType.Float);
            }
            if (loc.SpotCosCutoffLoc != -1)
            {
                Raylib.SetShaderValue(_shader, loc.SpotCosCutoffLoc, light.SpotCosCutoff, ShaderUniformDataType.Float);
            }
        }

        Raylib.BeginShaderMode(_shader);
    }

    /// <summary>
    /// Ends the custom lit preview shader mode.
    /// </summary>
    public void End()
    {
        if (IsReady)
        {
            Raylib.EndShaderMode();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isLoaded && _shader.Id != 0 && Raylib.IsWindowReady())
        {
            Raylib.UnloadShader(_shader);
            _shader = default;
            _isLoaded = false;
        }

        _isDisposed = true;
    }

    public const string VertexShaderSource = """
    #version 330

    // Input vertex attributes
    in vec3 vertexPosition;
    in vec2 vertexTexCoord;
    in vec3 vertexNormal;
    in vec4 vertexColor;

    // Input uniform values
    uniform mat4 mvp;

    // Output vertex attributes (to fragment shader)
    out vec3 fragPosition;
    out vec2 fragTexCoord;
    out vec3 fragNormal;
    out vec4 fragColor;

    void main()
    {
        fragPosition = vertexPosition;
        fragTexCoord = vertexTexCoord;
        fragNormal = normalize(vertexNormal);
        fragColor = vertexColor;

        gl_Position = mvp * vec4(vertexPosition, 1.0);
    }
    """;

    public const string FragmentShaderSource = """
    #version 330

    #define MAX_LIGHTS 16

    struct Light {
        int type; // 0 = Point, 1 = Spot
        vec3 position;
        vec3 direction;
        vec4 color;
        float intensity;
        float radius;
        float spotCosCutoff;
    };

    // Input vertex attributes (from vertex shader)
    in vec3 fragPosition;
    in vec2 fragTexCoord;
    in vec3 fragNormal;
    in vec4 fragColor;

    // Input uniform values
    uniform sampler2D texture0;
    uniform vec4 colDiffuse;

    uniform vec3 uViewPos;
    uniform int uNumLights;
    uniform Light lights[MAX_LIGHTS];

    uniform vec3 uAmbientColor;
    uniform float uAmbientIntensity;

    uniform int uFogEnabled;
    uniform vec3 uFogColor;
    uniform float uFogDensity;

    // Output fragment color
    out vec4 finalColor;

    void main()
    {
        vec4 texelColor = texture(texture0, fragTexCoord);
        vec4 albedo = texelColor * fragColor;

        vec3 N = normalize(fragNormal);
        vec3 V = normalize(uViewPos - fragPosition);

        vec3 ambient = uAmbientColor * uAmbientIntensity;
        vec3 accumLight = vec3(0.0);

        int activeCount = min(uNumLights, MAX_LIGHTS);
        for (int i = 0; i < activeCount; i++)
        {
            vec3 lightDir = lights[i].position - fragPosition;
            float dist = length(lightDir);
            if (dist < lights[i].radius && dist > 0.0001)
            {
                vec3 L = lightDir / dist;

                // Smooth window attenuation: atten = clamp(1.0 - pow(dist / radius, 2.0), 0.0, 1.0)^2
                float dOverR = dist / lights[i].radius;
                float window = clamp(1.0 - (dOverR * dOverR), 0.0, 1.0);
                float atten = window * window;

                // Spot cone factor
                float spotFactor = 1.0;
                if (lights[i].type == 1)
                {
                    float cosTheta = dot(-L, normalize(lights[i].direction));
                    float cosCutoff = lights[i].spotCosCutoff;
                    float innerCos = mix(cosCutoff, 1.0, 0.15);
                    spotFactor = clamp((cosTheta - cosCutoff) / max(innerCos - cosCutoff, 0.0001), 0.0, 1.0);
                }

                if (spotFactor > 0.0)
                {
                    // Lambertian diffuse
                    float nDotL = max(dot(N, L), 0.0);
                    if (nDotL > 0.0)
                    {
                        // Subtle Blinn-Phong specular highlight
                        vec3 H = normalize(L + V);
                        float nDotH = max(dot(N, H), 0.0);
                        float spec = pow(nDotH, 32.0) * 0.25;

                        vec3 lightContrib = lights[i].color.rgb * lights[i].intensity * atten * spotFactor * (nDotL + spec);
                        accumLight += lightContrib;
                    }
                }
            }
        }

        vec3 litColor = albedo.rgb * (ambient + accumLight);

        if (uFogEnabled != 0)
        {
            float fragDist = length(uViewPos - fragPosition);
            float fogFactor = clamp(1.0 - exp(-fragDist * uFogDensity), 0.0, 1.0);
            litColor = mix(litColor, uFogColor, fogFactor);
        }

        finalColor = vec4(litColor, albedo.a);
    }
    """;
}
