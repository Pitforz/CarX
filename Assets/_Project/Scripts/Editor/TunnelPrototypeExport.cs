using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Writes the procedural pieces as editable OBJ models (Blender, Max, Maya...) for artists to turn into real prefabs:
    /// per tunnel type a straight slab, 3 slabs, 45° and 90° elbows, a dead end; plus columns and a sample room.
    /// </summary>
    public static class TunnelPrototypeExport
    {
        public const string OutDir = "Assets/_Project/Tunnels/Prototypes";

        [MenuItem("CarX/Tunnels/Export Prototype Models (OBJ)", priority = 40)]
        static void Menu() { Export(); EditorUtility.RevealInFinder(OutDir); }

        public static void Export()
        {
            var lib = TunnelGraphAssets.GetLibrary();
            Directory.CreateDirectory(OutDir);
            var mats = TunnelMaterials.Resolve(lib.materials);
            int files = 0;
            var holder = new GameObject("_PrototypeExport") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var T in lib.types)
                {
                    if (T == null) continue;
                    string dir = OutDir + "/" + T.name;
                    Directory.CreateDirectory(dir);
                    float L = Mathf.Max(0.5f, T.segmentLength);
                    float half = T.section.Width * 0.5f;
                    float arm = half + L;   // elbow arms long enough for the mitre
                    files += Piece(holder, T, mats, dir + "/" + T.name + "_Straight.obj", new List<Vector3> { Vector3.zero, Vector3.forward * L }, null, false);
                    files += Piece(holder, T, mats, dir + "/" + T.name + "_Straight_x3.obj", new List<Vector3> { Vector3.zero, Vector3.forward * L, Vector3.forward * 2f * L, Vector3.forward * 3f * L }, null, false);
                    foreach (float ang in new[] { 45f, 90f })
                    {
                        var dir2 = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                        var corner = Vector3.forward * arm;
                        files += Piece(holder, T, mats, dir + "/" + T.name + "_Elbow" + ang.ToString("0") + ".obj",
                                       new List<Vector3> { Vector3.zero, corner, corner + dir2 * arm }, new List<Vector3> { corner }, false);
                    }
                    files += Piece(holder, T, mats, dir + "/" + T.name + "_DeadEnd.obj", new List<Vector3> { Vector3.zero, Vector3.forward * L }, null, true);
                }

                // columns and a sample room
                Directory.CreateDirectory(OutDir + "/Rooms");
                foreach (var shape in new[] { ColumnShape.Square, ColumnShape.Round })
                {
                    var kit = new MeshKit(5);
                    new ColumnSpec { shape = shape, size = shape == ColumnShape.Round ? 0.7f : 0.6f }.Emit(kit, 0, 0f, 3f);
                    WriteObj(OutDir + "/Rooms/Column_" + shape + ".obj", kit.ToMesh("Column_" + shape), mats);
                    files++;
                }
                var roomGo = new GameObject("Room") { hideFlags = HideFlags.HideAndDontSave };
                roomGo.transform.SetParent(holder.transform, false);
                var room = roomGo.AddComponent<TunnelRoom>();
                room.corners = GraphNode.DefaultRoom();
                room.height = 3f;
                room.materials = mats;
                List<int> kind; int ceil;
                if (lib.types.Count > 0) room.doors.Add(new RoomDoor { wall = 0, along = Vector2.Distance(room.corners[0], room.corners[1]) * 0.5f, outline = lib.types[0].section.BaseOutline(out kind, out ceil) });
                room.Rebuild();
                WriteObj(OutDir + "/Rooms/Room_Octagon.obj", roomGo.GetComponentInChildren<MeshFilter>().sharedMesh, mats);
                files++;
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
            AssetDatabase.Refresh();
            Debug.Log("CarX: prototype models exported: " + files + " files in " + OutDir);
        }

        static int Piece(GameObject holder, TunnelType T, TunnelMaterials mats, string file, List<Vector3> samples, List<Vector3> miters, bool capEnd)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(file)) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(holder.transform, false);
            var p = go.AddComponent<TunnelPath>();
            p.section = T.section.Clone();
            p.segmentLength = T.segmentLength;
            p.slabThickness = T.slabThickness;
            p.minJointGap = T.minJointGap;
            p.jointSink = T.jointSink;
            p.edgeBevel = T.edgeBevel;
            p.dirtLine = T.dirtLine;
            p.channelWater = T.channelWater;
            p.waterDepth = T.waterDepth;
            p.seamless = T.seamless;
            p.materials = T.materials != null ? T.materials : mats;
            p.startAtWall = true;
            p.endAtWall = !capEnd;
            p.capEnd = capEnd;
            p.drivenMiters = miters ?? new List<Vector3>();
            p.drivenSamples = samples;
            p.Rebuild(true);
            var meshes = new List<Mesh>();
            var names = new List<string>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                meshes.Add(mf.sharedMesh);
                names.Add(mf.gameObject.name);
            }
            WriteObj(file, meshes, names, p.materials);
            return 1;
        }

        static void WriteObj(string file, Mesh mesh, TunnelMaterials mats)
        {
            WriteObj(file, new List<Mesh> { mesh }, new List<string> { Path.GetFileNameWithoutExtension(file) }, mats);
        }

        /// <summary>OBJ is right-handed: x is mirrored and the triangle order flipped, as Unity's own importer expects.</summary>
        static void WriteObj(string file, List<Mesh> meshes, List<string> names, TunnelMaterials mats)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            string mtl = Path.GetFileNameWithoutExtension(file) + ".mtl";
            sb.AppendLine("# CarX procedural prototype (AI-assisted tool output), units = metres");
            sb.AppendLine("mtllib " + mtl);
            var matNames = new HashSet<string>();
            var set = mats != null ? mats.TunnelSet() : null;
            int baseIndex = 1;
            for (int m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
                sb.AppendLine("o " + names[m]);
                foreach (var p in v) sb.AppendLine(string.Format(ci, "v {0:0.#####} {1:0.#####} {2:0.#####}", -p.x, p.y, p.z));
                foreach (var p in n) sb.AppendLine(string.Format(ci, "vn {0:0.####} {1:0.####} {2:0.####}", -p.x, p.y, p.z));
                foreach (var p in uv) sb.AppendLine(string.Format(ci, "vt {0:0.#####} {1:0.#####}", p.x, p.y));
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var t = mesh.GetTriangles(s);
                    if (t.Length == 0) continue;
                    string mat = set != null && s < set.Length && set[s] != null ? set[s].name : "Sub" + s;
                    if (meshes.Count > 1 && names[m].Contains("Water")) mat = mats != null && mats.water != null ? mats.water.name : "Water";
                    mat = mat.Replace(' ', '_');
                    matNames.Add(mat);
                    sb.AppendLine("usemtl " + mat);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        int a = t[i] + baseIndex, b = t[i + 2] + baseIndex, c = t[i + 1] + baseIndex;
                        sb.AppendLine(string.Format(ci, "f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a, b, c));
                    }
                }
                baseIndex += v.Length;
            }
            File.WriteAllText(file, sb.ToString());
            var ms = new StringBuilder();
            foreach (var name in matNames)
            {
                ms.AppendLine("newmtl " + name);
                ms.AppendLine("Kd 0.6 0.6 0.6");
                ms.AppendLine();
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(file), mtl), ms.ToString());
        }
    }
}
