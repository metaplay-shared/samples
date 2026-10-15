using Metaplay.Core.Math;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Game.Logic
{
    /// <summary>
    /// The course parts' authoring format: <c>GameConfigSource/CourseParts.json</c>, one JSON array of part
    /// objects. The config build parses it, and the part editor in the developer course tools writes a draft
    /// back out so it can be pasted in.
    /// <para>
    /// Only <c>System.Text.Json</c>'s document and encoder types are used, not its reflection-based
    /// serializer, because this code is also compiled into the trimmed browser client.
    /// </para>
    /// </summary>
    public static class CoursePartJson
    {
        /// <summary>The name the file is read under, used as the prefix of every error message.</summary>
        const string FileName = "CourseParts.json";

        /// <summary>Strings are escaped only where JSON requires it, so ids and notes stay readable in the file.</summary>
        static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        /// <summary>Every part in <paramref name="json"/>, in file order.</summary>
        /// <exception cref="FormatException">The text is not a valid parts file; the message names the part and the field.</exception>
        public static IReadOnlyList<CoursePartInfo> ParseAll(string json)
            => ParseAll(Encoding.UTF8.GetBytes(json ?? ""));

        /// <summary>Every part in the UTF-8 file contents <paramref name="utf8"/>, in file order.</summary>
        /// <exception cref="FormatException">The bytes are not a valid parts file; the message names the part and the field.</exception>
        public static IReadOnlyList<CoursePartInfo> ParseAll(ReadOnlyMemory<byte> utf8)
        {
            // A byte-order mark is not JSON, but it is what some editors save a UTF-8 file with.
            ReadOnlySpan<byte> bom = stackalloc byte[] { 0xEF, 0xBB, 0xBF };
            if (utf8.Span.StartsWith(bom))
                utf8 = utf8.Slice(bom.Length);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(utf8);
            }
            catch (JsonException malformed)
            {
                throw new FormatException($"{FileName}: the file is not valid JSON: {malformed.Message}", malformed);
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    throw new FormatException($"{FileName}: the file must hold one JSON array of part objects, not {Describe(root.ValueKind)}.");

                List<CoursePartInfo>       parts  = new List<CoursePartInfo>();
                Dictionary<string, int>    seenAt = new Dictionary<string, int>(StringComparer.Ordinal);
                int                        index  = 0;
                foreach (JsonElement element in root.EnumerateArray())
                {
                    CoursePartInfo part = ParsePart(element, index);
                    if (seenAt.TryGetValue(part.Id.Value, out int earlier))
                        throw new FormatException($"{PartLabel(index, part.Id.Value)}: the id is already used by part [{earlier}]. A part id names one part.");

                    seenAt.Add(part.Id.Value, index);
                    parts.Add(part);
                    index++;
                }

                return parts;
            }
        }

        /// <summary>
        /// One part object. <paramref name="index"/> is its place in the file's array, counted from 0 like the
        /// arrays inside it, and is what the error messages name it by until its id is known.
        /// </summary>
        /// <exception cref="FormatException">The object is not a valid part; the message names the part and the field.</exception>
        static CoursePartInfo ParsePart(JsonElement element, int index)
        {
            string label = PartLabel(index, null);
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{label}: a part must be a JSON object, not {Describe(element.ValueKind)}.");

            // The id first, so every later message can name the part by it.
            string idText = RequiredString(element, "id", label);
            label = PartLabel(index, idText);

            CoursePartId id;
            try
            {
                id = CoursePartId.FromString(idText);
            }
            catch (ArgumentException refused)
            {
                throw new FormatException($"{label}: id '{idText}' is not one a part can carry: {refused.Message}", refused);
            }

            // An unknown property is an error, so a misspelt name cannot silently drop a value.
            RejectUnknownProperties(element, label, "",
                "id", "notes", "positions", "isSetPiece", "ground", "hazards", "bridges", "decorations", "vines");

            // Notes are free text for whoever edits the file next, and are not carried into the config.
            if (element.TryGetProperty("notes", out JsonElement notes) && notes.ValueKind != JsonValueKind.String)
                throw new FormatException($"{label}: notes must be a string, not {Describe(notes.ValueKind)}.");

            bool isSetPiece = RequiredBool(element, "isSetPiece", label);

            List<CoursePartPosition> positions = new List<CoursePartPosition>();
            int positionIndex = 0;
            foreach (JsonElement position in OptionalArray(element, "positions", label))
            {
                positions.Add(ParsePosition(position, $"positions[{positionIndex}]", label));
                positionIndex++;
            }

            List<GroundSegment> ground = new List<GroundSegment>();
            int groundIndex = 0;
            foreach (JsonElement segment in OptionalArray(element, "ground", label))
            {
                string path = $"ground[{groundIndex}]";
                RequireObject(segment, path, label);
                RejectUnknownProperties(segment, label, path + ".", "startX", "endX", "topY", "pit");
                ground.Add(new GroundSegment(
                    RequiredMetres(segment, "startX", path, label),
                    RequiredMetres(segment, "endX",   path, label),
                    RequiredMetres(segment, "topY",   path, label),
                    OptionalBool(segment, "pit", path, label)));
                groundIndex++;
            }

            List<HazardSpan> hazards = new List<HazardSpan>();
            int hazardIndex = 0;
            foreach (JsonElement hazard in OptionalArray(element, "hazards", label))
            {
                string path = $"hazards[{hazardIndex}]";
                RequireObject(hazard, path, label);
                RejectUnknownProperties(hazard, label, path + ".", "startX", "endX");
                hazards.Add(new HazardSpan(
                    RequiredMetres(hazard, "startX", path, label),
                    RequiredMetres(hazard, "endX",   path, label)));
                hazardIndex++;
            }

            List<CoursePartBridge> bridges = new List<CoursePartBridge>();
            int bridgeIndex = 0;
            foreach (JsonElement bridge in OptionalArray(element, "bridges", label))
            {
                string path = $"bridges[{bridgeIndex}]";
                RequireObject(bridge, path, label);
                RejectUnknownProperties(bridge, label, path + ".", "startX", "endX");
                bridges.Add(new CoursePartBridge(
                    RequiredMetres(bridge, "startX", path, label),
                    RequiredMetres(bridge, "endX",   path, label)));
                bridgeIndex++;
            }

            List<CoursePartDecoration> decorations = new List<CoursePartDecoration>();
            int decorationIndex = 0;
            foreach (JsonElement decoration in OptionalArray(element, "decorations", label))
            {
                decorations.Add(ParseDecoration(decoration, $"decorations[{decorationIndex}]", label));
                decorationIndex++;
            }

            List<CoursePartVine> vines = new List<CoursePartVine>();
            int vineIndex = 0;
            foreach (JsonElement vine in OptionalArray(element, "vines", label))
            {
                string path = $"vines[{vineIndex}]";
                RequireObject(vine, path, label);
                RejectUnknownProperties(vine, label, path + ".", "anchorX", "anchorY", "platformStartX", "platformEndX", "platformTopY");
                vines.Add(new CoursePartVine(
                    RequiredMetres(vine, "anchorX",        path, label),
                    RequiredMetres(vine, "anchorY",        path, label),
                    RequiredMetres(vine, "platformStartX", path, label),
                    RequiredMetres(vine, "platformEndX",   path, label),
                    RequiredMetres(vine, "platformTopY",   path, label)));
                vineIndex++;
            }

            return new CoursePartInfo(
                id:            id,
                ground:        ground,
                hazards:       hazards,
                positions:     positions,
                isSetPiece: isSetPiece,
                bridges:       bridges,
                decorations:   decorations,
                vines:         vines);
        }

        /// <summary>
        /// One decoration object: a kind, a part-local X, a layer by name and an optional flip.
        /// </summary>
        static CoursePartDecoration ParseDecoration(JsonElement element, string path, string label)
        {
            RequireObject(element, path, label);
            RejectUnknownProperties(element, label, path + ".", "kind", "x", "layer", "flip");

            // Whether the kind is a well-formed kind id and whether the X stands on ground are the part's own
            // rules (CoursePartInfo.Validate), reported with the rest of them.
            JsonElement kind = Required(element, "kind", path + ".", label);
            if (kind.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(kind.GetString()))
                throw new FormatException($"{label}: {path}.kind must be a kind id string, not {(kind.ValueKind == JsonValueKind.String ? "an empty one" : Describe(kind.ValueKind))}.");

            F32 x = RequiredMetres(element, "x", path, label);

            JsonElement layerElement = Required(element, "layer", path + ".", label);
            CourseDecorationLayer? layer = null;
            if (layerElement.ValueKind == JsonValueKind.String)
            {
                foreach (CourseDecorationLayer candidate in Enum.GetValues<CourseDecorationLayer>())
                {
                    if (string.Equals(candidate.ToString(), layerElement.GetString(), StringComparison.Ordinal))
                        layer = candidate;
                }
            }
            if (layer == null)
                throw new FormatException($"{label}: {path}.layer is {layerElement.GetRawText()}, which is not a layer. Expected one of {string.Join(", ", Enum.GetNames<CourseDecorationLayer>())}.");

            bool flip = OptionalBool(element, "flip", path, label);

            return new CoursePartDecoration(kind.GetString(), x, layer.Value, flip);
        }

        /// <summary>
        /// <paramref name="part"/> as one part object, indented two spaces a level from column 0, ready to be
        /// pasted into the file's array. <paramref name="notes"/> is written as the object's <c>notes</c> when
        /// given.
        /// </summary>
        public static string Write(CoursePartInfo part, string notes = null)
        {
            StringBuilder json = new StringBuilder();
            AppendPart(json, part, notes, "");
            return json.ToString();
        }

        static void AppendPart(StringBuilder json, CoursePartInfo part, string notes, string indent)
        {
            string inner = indent + "  ";

            json.Append("{\n");
            json.Append(inner).Append("\"id\": ").Append(Quote(part.Id?.Value ?? "")).Append(",\n");
            if (notes != null)
                json.Append(inner).Append("\"notes\": ").Append(Quote(notes)).Append(",\n");

            List<string> positions = new List<string>();
            foreach (CoursePartPosition position in part.Positions)
                positions.Add(Quote(position.ToString()));
            json.Append(inner).Append("\"positions\": [").Append(string.Join(", ", positions)).Append("],\n");

            json.Append(inner).Append("\"isSetPiece\": ").Append(part.IsSetPiece ? "true" : "false").Append(",\n");

            List<string> ground = new List<string>();
            foreach (GroundSegment segment in part.Ground)
            {
                string pit = segment.IsPit ? ", \"pit\": true" : "";
                ground.Add($"{{ \"startX\": {Metres(segment.StartX)}, \"endX\": {Metres(segment.EndX)}, \"topY\": {Metres(segment.TopY)}{pit} }}");
            }
            AppendArray(json, inner, "ground", ground, isLast: false);

            List<string> hazards = new List<string>();
            foreach (HazardSpan hazard in part.Hazards)
                hazards.Add($"{{ \"startX\": {Metres(hazard.StartX)}, \"endX\": {Metres(hazard.EndX)} }}");
            AppendArray(json, inner, "hazards", hazards, isLast: false);

            List<string> bridges = new List<string>();
            foreach (CoursePartBridge bridge in part.Bridges)
                bridges.Add($"{{ \"startX\": {Metres(bridge.StartX)}, \"endX\": {Metres(bridge.EndX)} }}");
            List<CoursePartDecoration> partDecorations = part.Decorations;
            List<CoursePartVine>       partVines       = part.Vines;
            AppendArray(json, inner, "bridges", bridges, isLast: partDecorations.Count == 0 && partVines.Count == 0);

            if (partDecorations.Count > 0)
            {
                List<string> decorations = new List<string>();
                foreach (CoursePartDecoration decoration in partDecorations)
                {
                    string flip = decoration.Flip ? ", \"flip\": true" : "";
                    decorations.Add($"{{ \"kind\": {Quote(decoration.Kind ?? "")}, \"x\": {Metres(decoration.X)}, \"layer\": {Quote(decoration.Layer.ToString())}{flip} }}");
                }
                AppendArray(json, inner, "decorations", decorations, isLast: partVines.Count == 0);
            }

            if (partVines.Count > 0)
            {
                List<string> vines = new List<string>();
                foreach (CoursePartVine vine in partVines)
                {
                    vines.Add($"{{ \"anchorX\": {Metres(vine.AnchorX)}, \"anchorY\": {Metres(vine.AnchorY)}, \"platformStartX\": {Metres(vine.PlatformStartX)}, \"platformEndX\": {Metres(vine.PlatformEndX)}, \"platformTopY\": {Metres(vine.PlatformTopY)} }}");
                }
                AppendArray(json, inner, "vines", vines, isLast: true);
            }

            json.Append(indent).Append('}');
        }

        /// <summary>An array of short objects, one per line; an empty one stays on the property's own line.</summary>
        static void AppendArray(StringBuilder json, string indent, string name, List<string> elements, bool isLast)
        {
            json.Append(indent).Append('"').Append(name).Append("\": [");
            if (elements.Count > 0)
            {
                json.Append('\n');
                for (int ndx = 0; ndx < elements.Count; ndx++)
                {
                    json.Append(indent).Append("  ").Append(elements[ndx]);
                    json.Append(ndx < elements.Count - 1 ? ",\n" : "\n");
                }
                json.Append(indent);
            }
            json.Append(']');
            json.Append(isLast ? "\n" : ",\n");
        }

        /// <summary>A length as the shortest decimal text that parses back to it, without a fraction when it is whole.</summary>
        static string Metres(F32 value)
        {
            string text = value.ToString();
            return text.EndsWith(".0", StringComparison.Ordinal) ? text.Substring(0, text.Length - 2) : text;
        }

        static string Quote(string text) => "\"" + JsonEncodedText.Encode(text, Encoder).ToString() + "\"";

        static string PartLabel(int index, string id)
            => id == null ? $"{FileName}: part [{index}]" : $"{FileName}: part [{index}] ('{id}')";

        static void RejectUnknownProperties(JsonElement element, string label, string pathPrefix, params string[] known)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (Array.IndexOf(known, property.Name) < 0)
                    throw new FormatException($"{label}: {pathPrefix}{property.Name} is not a property the format has. Expected {string.Join(", ", known)}.");
            }
        }

        static JsonElement Required(JsonElement element, string name, string path, string label)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                throw new FormatException($"{label}: {path}{name} is missing.");
            return value;
        }

        static string RequiredString(JsonElement element, string name, string label)
        {
            JsonElement value = Required(element, name, "", label);
            if (value.ValueKind != JsonValueKind.String)
                throw new FormatException($"{label}: {name} must be a string, not {Describe(value.ValueKind)}.");

            string text = value.GetString();
            if (string.IsNullOrEmpty(text))
                throw new FormatException($"{label}: {name} is empty.");
            return text;
        }

        static bool RequiredBool(JsonElement element, string name, string label)
        {
            JsonElement value = Required(element, name, "", label);
            if (value.ValueKind == JsonValueKind.True)
                return true;
            if (value.ValueKind == JsonValueKind.False)
                return false;
            throw new FormatException($"{label}: {name} must be true or false, not {Describe(value.ValueKind)}.");
        }

        /// <summary>A boolean that is false when the property is absent.</summary>
        static bool OptionalBool(JsonElement element, string name, string path, string label)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return false;
            if (value.ValueKind == JsonValueKind.True)
                return true;
            if (value.ValueKind == JsonValueKind.False)
                return false;
            throw new FormatException($"{label}: {path}.{name} must be true or false, not {Describe(value.ValueKind)}.");
        }

        /// <summary>A list property's elements, or none when the property is absent.</summary>
        static IEnumerable<JsonElement> OptionalArray(JsonElement element, string name, string label)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return Array.Empty<JsonElement>();
            if (value.ValueKind != JsonValueKind.Array)
                throw new FormatException($"{label}: {name} must be an array, not {Describe(value.ValueKind)}.");
            return value.EnumerateArray();
        }

        static void RequireObject(JsonElement element, string path, string label)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{label}: {path} must be an object, not {Describe(element.ValueKind)}.");
        }

        /// <summary>A number of metres, parsed from the number's own text so the result is the same on every machine.</summary>
        static F32 RequiredMetres(JsonElement element, string name, string path, string label)
        {
            JsonElement value = Required(element, name, path + ".", label);
            if (value.ValueKind != JsonValueKind.Number)
                throw new FormatException($"{label}: {path}.{name} must be a number of metres, not {Describe(value.ValueKind)}.");

            string text = value.GetRawText();
            try
            {
                return F32.Parse(text);
            }
            catch (Exception refused) when (refused is ArgumentException || refused is OverflowException || refused is FormatException)
            {
                throw new FormatException($"{label}: {path}.{name} is {text}, which is not a plain decimal number of metres within range.", refused);
            }
        }

        static CoursePartPosition ParsePosition(JsonElement element, string path, string label)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string name = element.GetString();
                foreach (CoursePartPosition position in Enum.GetValues<CoursePartPosition>())
                {
                    if (string.Equals(position.ToString(), name, StringComparison.Ordinal))
                        return position;
                }
            }

            throw new FormatException($"{label}: {path} is {element.GetRawText()}, which is not a position. Expected one of {string.Join(", ", Enum.GetNames<CoursePartPosition>())}.");
        }

        static string Describe(JsonValueKind kind)
        {
            switch (kind)
            {
                case JsonValueKind.Object: return "an object";
                case JsonValueKind.Array:  return "an array";
                case JsonValueKind.String: return "a string";
                case JsonValueKind.Number: return "a number";
                case JsonValueKind.True:   return "true";
                case JsonValueKind.False:  return "false";
                case JsonValueKind.Null:   return "null";
                default:                   return kind.ToString();
            }
        }
    }
}
