using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;
using System.Numerics;

string inputPath = args[0];
string outputPath = "output.p3d";

// Check file exists here

P3DFile p3dFile = new(inputPath);

// Get offsets here
var offset = new Vector3(100, -5.5f, 200);

foreach (var staticEntity in p3dFile.GetChunksOfType<NetP3DLib.P3D.Chunks.StaticEntityChunk>()) {
	foreach (var mesh in staticEntity.GetChunksOfType<NetP3DLib.P3D.Chunks.MeshChunk>()) {
		foreach (var oldPrimitiveGroup in mesh.GetChunksOfType<NetP3DLib.P3D.Chunks.OldPrimitiveGroupChunk>()) {
			var positionList = oldPrimitiveGroup.GetFirstChunkOfType<NetP3DLib.P3D.Chunks.PositionListChunk>();

			for (int i = 0; i < positionList.Positions.Count; i++) {
				positionList.Positions[i] += offset;
			}
		}
	}
}

p3dFile.Write(outputPath);