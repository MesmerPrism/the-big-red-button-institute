using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using TMPro;
using TheBigRedButtonInstitute.IndirectParticles;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TheBigRedButtonInstitute.Editor
{
    public sealed class BigRedButtonParticleCounterPlayModeEvidence
    {
        private const int Width = 1024;
        private const int Height = 512;
        private const string EvidenceDirectory = "Builds/PlayModeEvidence";

        [UnityTest]
        public IEnumerator ParticleCounterRendersInPlayMode()
        {
            yield return new EnterPlayMode();

            Directory.CreateDirectory(EvidenceDirectory);
            var camera = CreateEvidenceCamera();
            var display = CreateParticleCounterDisplay();
            var variants = new[]
            {
                new Variant("particle-size-3", 3f, 0.9f),
                new Variant("particle-size-5", 5f, 0.9f),
                new Variant("particle-size-7", 7f, 0.9f)
            };

            using var report = new StreamWriter(Path.Combine(EvidenceDirectory, "particle-counter-playmode-summary.json"));
            report.WriteLine("{");
            report.WriteLine("  \"schema\": \"brb.particle_counter_playmode_evidence.v1\",");
            report.WriteLine("  \"number_text\": \"123\",");
            report.WriteLine("  \"frames\": [");
            var reportState = new ReportState();

            for (int i = 0; i < variants.Length; i++)
            {
                Variant variant = variants[i];
                display.SetParticleVisuals(variant.ParticleSize, variant.RadialClip);
                display.SetNumberText("123");
                display.SetPresentation(new Color(1f, 0.05f, 0.03f, 1f), 1f);

                yield return null;
                yield return new WaitForEndOfFrame();

                string imagePath = Path.Combine(EvidenceDirectory, $"{variant.Name}.png");
                int litPixels = Capture(camera, imagePath);
                WriteFrame(report, reportState, variant.Name, variant.ParticleSize, variant.RadialClip, litPixels, imagePath);

                Assert.Greater(litPixels, 250, $"{variant.Name} should render visible particles.");
            }

            display.SetParticleVisuals(5f, 0.9f);
            display.SetDigitMorphOptions(enabled: true, duration: 0.35f);
            yield return CaptureMorphFrame(
                display,
                camera,
                report,
                reportState,
                "456",
                "particle-morph-mid-123-to-456",
                0.18f);
            yield return CaptureMorphFrame(
                display,
                camera,
                report,
                reportState,
                "456",
                "particle-morph-final-456",
                0.6f);

            display.SetNumberText("9");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return CaptureMorphFrame(
                display,
                camera,
                report,
                reportState,
                "10",
                "particle-morph-mid-9-to-10",
                0.18f);
            yield return CaptureMorphFrame(
                display,
                camera,
                report,
                reportState,
                "10",
                "particle-morph-final-10",
                0.6f);

            if (reportState.WroteFrame)
                report.WriteLine();
            report.WriteLine("  ]");
            report.WriteLine("}");

            yield return new ExitPlayMode();
        }

        private static Camera CreateEvidenceCamera()
        {
            var cameraObject = new GameObject("Particle Counter Evidence Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 0.025f;
            camera.nearClipPlane = 0.001f;
            camera.farClipPlane = 10f;
            camera.transform.position = new Vector3(0f, 0f, -0.1f);
            camera.transform.rotation = Quaternion.identity;
            camera.enabled = true;
            return camera;
        }

        private static BigRedButtonParticlePressCounterDisplay CreateParticleCounterDisplay()
        {
            var counterRoot = new GameObject("Button Press Counter Evidence Root");
            counterRoot.transform.position = Vector3.zero;

            var canvasObject = new GameObject("CounterCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvasTransform = (RectTransform)canvasObject.transform;
            canvasTransform.SetParent(counterRoot.transform, false);
            canvasTransform.sizeDelta = new Vector2(720f, 280f);
            canvasTransform.localScale = Vector3.one * 0.0001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var textObject = new GameObject("CountText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textTransform = (RectTransform)textObject.transform;
            textTransform.SetParent(canvasTransform, false);
            textTransform.sizeDelta = new Vector2(680f, 240f);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = "123";
            text.fontSize = 210f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.82f, 0.22f, 0.22f, 1f);
            text.raycastTarget = false;
            text.enabled = false;

            var display = counterRoot.AddComponent<BigRedButtonParticlePressCounterDisplay>();
            display.Configure(text);
            display.SetProceduralFallbackDigits(enabled: true, pointsPerDigit: 10000);
            return display;
        }

        private static IEnumerator CaptureMorphFrame(
            BigRedButtonParticlePressCounterDisplay display,
            Camera camera,
            StreamWriter report,
            ReportState reportState,
            string targetNumberText,
            string frameName,
            float delaySeconds)
        {
            display.SetNumberText(targetNumberText);
            yield return new WaitForSecondsRealtime(delaySeconds);
            yield return new WaitForEndOfFrame();

            string imagePath = Path.Combine(EvidenceDirectory, $"{frameName}.png");
            int litPixels = Capture(camera, imagePath);
            WriteFrame(report, reportState, frameName, 5f, 0.9f, litPixels, imagePath);
            Assert.Greater(litPixels, 250, $"{frameName} should render visible particles.");
        }

        private static void WriteFrame(
            StreamWriter report,
            ReportState reportState,
            string name,
            float particleSize,
            float radialClip,
            int litPixels,
            string imagePath)
        {
            if (reportState.WroteFrame)
                report.WriteLine(",");

            report.Write("    ");
            report.Write("{ ");
            report.Write($"\"name\": \"{name}\", ");
            report.Write($"\"particle_size_local\": {particleSize.ToString(CultureInfo.InvariantCulture)}, ");
            report.Write($"\"radial_clip\": {radialClip.ToString(CultureInfo.InvariantCulture)}, ");
            report.Write($"\"lit_pixels\": {litPixels}, ");
            report.Write($"\"path\": \"{imagePath.Replace("\\", "/")}\" ");
            report.Write("}");
            reportState.WroteFrame = true;
        }

        private static int Capture(Camera camera, string path)
        {
            var renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return CountLitPixels(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static int CountLitPixels(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int litPixels = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.r > 12 || pixel.g > 12 || pixel.b > 12)
                    litPixels++;
            }

            return litPixels;
        }

        private readonly struct Variant
        {
            public Variant(string name, float particleSize, float radialClip)
            {
                Name = name;
                ParticleSize = particleSize;
                RadialClip = radialClip;
            }

            public string Name { get; }
            public float ParticleSize { get; }
            public float RadialClip { get; }
        }

        private sealed class ReportState
        {
            public bool WroteFrame { get; set; }
        }
    }
}
