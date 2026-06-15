using System;
using System.Globalization;
using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [Serializable]
    public sealed class BigRedButtonRuntimeLayoutConfig
    {
        public const string Schema = "brb.runtime_layout.v1";
        public const string DefaultsRelativePath = "runtime_config/brb_layout_defaults.json";
        public const string RuntimeOverrideRelativePath = "runtime_config/brb_layout_runtime.json";

        public const float DefaultCounterCanvasLocalY = 0.0315f;
        public const float DefaultButtonHeightMeters = 0.36f;
        public const float DefaultButtonDistanceFromHeadMeters = 0.48f;
        public const float DefaultButtonVerticalOffsetFromHeadMeters = -0.32f;
        public const float DefaultMinimumButtonWorldY = 0.54f;
        public const float DefaultAbsoluteButtonWorldY = 1.33f;

        public string schema = Schema;
        public float counter_canvas_local_y_m = DefaultCounterCanvasLocalY;
        public float button_height_m = DefaultButtonHeightMeters;
        public float button_distance_from_head_m = DefaultButtonDistanceFromHeadMeters;
        public float button_vertical_offset_from_head_m = DefaultButtonVerticalOffsetFromHeadMeters;
        public float minimum_button_world_y_m = DefaultMinimumButtonWorldY;
        public bool use_absolute_button_world_y;
        public float absolute_button_world_y_m = DefaultAbsoluteButtonWorldY;
        public bool place_button_on_startup = true;
        public bool keep_button_in_front_of_head = true;

        public BigRedButtonRuntimeLayoutConfig Clone()
        {
            return new BigRedButtonRuntimeLayoutConfig
            {
                schema = string.IsNullOrWhiteSpace(schema) ? Schema : schema,
                counter_canvas_local_y_m = counter_canvas_local_y_m,
                button_height_m = button_height_m,
                button_distance_from_head_m = button_distance_from_head_m,
                button_vertical_offset_from_head_m = button_vertical_offset_from_head_m,
                minimum_button_world_y_m = minimum_button_world_y_m,
                use_absolute_button_world_y = use_absolute_button_world_y,
                absolute_button_world_y_m = absolute_button_world_y_m,
                place_button_on_startup = place_button_on_startup,
                keep_button_in_front_of_head = keep_button_in_front_of_head
            };
        }

        public void Normalize()
        {
            schema = string.IsNullOrWhiteSpace(schema) ? Schema : schema.Trim();
            counter_canvas_local_y_m = SanitizeFinite(counter_canvas_local_y_m, DefaultCounterCanvasLocalY);
            button_height_m = Mathf.Clamp(SanitizeFinite(button_height_m, DefaultButtonHeightMeters), 0.05f, 2f);
            button_distance_from_head_m = Mathf.Clamp(SanitizeFinite(button_distance_from_head_m, DefaultButtonDistanceFromHeadMeters), 0.05f, 5f);
            button_vertical_offset_from_head_m = Mathf.Clamp(SanitizeFinite(button_vertical_offset_from_head_m, DefaultButtonVerticalOffsetFromHeadMeters), -3f, 3f);
            minimum_button_world_y_m = Mathf.Clamp(SanitizeFinite(minimum_button_world_y_m, DefaultMinimumButtonWorldY), -3f, 5f);
            absolute_button_world_y_m = Mathf.Clamp(SanitizeFinite(absolute_button_world_y_m, DefaultAbsoluteButtonWorldY), -3f, 5f);
        }

        public string ToCompactString()
        {
            return
                $"counterY={Format(counter_canvas_local_y_m)} " +
                $"buttonHeight={Format(button_height_m)} " +
                $"distance={Format(button_distance_from_head_m)} " +
                $"offsetY={Format(button_vertical_offset_from_head_m)} " +
                $"minY={Format(minimum_button_world_y_m)} " +
                $"absoluteY={(use_absolute_button_world_y ? Format(absolute_button_world_y_m) : "off")} " +
                $"startup={(place_button_on_startup ? 1 : 0)} " +
                $"follow={(keep_button_in_front_of_head ? 1 : 0)}";
        }

        public static string ToJson(BigRedButtonRuntimeLayoutConfig config)
        {
            var normalized = (config ?? new BigRedButtonRuntimeLayoutConfig()).Clone();
            normalized.Normalize();
            return JsonUtility.ToJson(normalized, true);
        }

        public static bool TryFromJson(
            string json,
            BigRedButtonRuntimeLayoutConfig fallback,
            out BigRedButtonRuntimeLayoutConfig config,
            out string error)
        {
            config = (fallback ?? new BigRedButtonRuntimeLayoutConfig()).Clone();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON text is empty.";
                return false;
            }

            try
            {
                JsonUtility.FromJsonOverwrite(json, config);
                config.Normalize();
                return true;
            }
            catch (Exception ex)
            {
                config = (fallback ?? new BigRedButtonRuntimeLayoutConfig()).Clone();
                config.Normalize();
                error = ex.Message;
                return false;
            }
        }

        static float SanitizeFinite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        static string Format(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
