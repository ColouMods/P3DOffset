using System.Numerics;
using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;

namespace P3DOffset;

static class Program {
	// Set constants.
	const float deg2Rad = MathF.PI / 180;

	// Initialise static variables.
	static Vector3 translation = new Vector3(0, 0, 0);
	static Vector3 rotation = new Vector3(0, 0, 0);
	static Matrix4x4 rotMtrx;
	static Matrix4x4 transMtrx;
	static Matrix4x4 transform;

	public static void Main(string[] args)
	{
		// Initialise variables.
		var inputPath = string.Empty;
		var outputPath = string.Empty;
		var forceOverwrite = false;
		
		// Get variable values from command line arguments.
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "-i":
					inputPath = GetArgValue(args, i);
					break;
				case "-o":
					outputPath = GetArgValue(args, i);
					break;
				case "-f":
					forceOverwrite = true;
					break;
				case "-x":
					translation.X = float.Parse(GetArgValue(args, i));
					break;
				case "-y":
					translation.Y = float.Parse(GetArgValue(args, i));
					break;
				case "-z":
					translation.Z = float.Parse(GetArgValue(args, i));
					break;
				case "-rx":
					rotation.X = float.Parse(GetArgValue(args, i));
					break;
				case "-ry":
					rotation.Y = float.Parse(GetArgValue(args, i));
					break;
				case "-rz":
					rotation.Z = float.Parse(GetArgValue(args, i));
					break;
			}
		}
		
		// Convert input euler angles to rotation matrix.
		rotMtrx = Matrix4x4.CreateFromYawPitchRoll(rotation.Y * deg2Rad, rotation.X * deg2Rad, rotation.Z * deg2Rad);
		// Convret input translation to translation matrix.
		transMtrx = Matrix4x4.CreateTranslation(translation);
		// Combine rotation matrix and translation matrix into transformation matrix.
		transform = rotMtrx * transMtrx;

		// Check input and output files have been specified.
		if (string.IsNullOrEmpty(inputPath))
		{
			Console.WriteLine("Error: No input file specified.");
			Environment.Exit(2);
		}

		if (string.IsNullOrEmpty(outputPath))
		{
			Console.WriteLine("Error: No output file specified.");
			Environment.Exit(2);
		}

		// Check input file exists.
		if (File.Exists(inputPath) == false)
		{
			Console.WriteLine("Error: Input file does not exist.");
			Environment.Exit(3);
		}

		// Create P3DFile Object
		Console.WriteLine($"Reading {inputPath}...");
		P3DFile p3dFile = new(inputPath);

		// Offset Chunks //
		// Static Entity (0x3F00000)
		foreach (var staticEntity in p3dFile.GetChunksOfType<StaticEntityChunk>())
		{
			OffsetMeshes(staticEntity);
		}
		
		// Static Phys (0x3F00001)
		foreach (var staticPhys in p3dFile.GetChunksOfType<StaticPhysChunk>())
		{
			OffsetCollisionObjects(staticPhys);
		}

		// Dyna Phys (0x3F00002)
		foreach (var dynaPhys in p3dFile.GetChunksOfType<DynaPhysChunk>())
		{
			OffsetInstanceLists(dynaPhys);
		}
		
		// Inst Stat Entity (0x3F00008)
		foreach (var instStatEntity in p3dFile.GetChunksOfType<InstStatEntityChunk>())
		{
			OffsetInstanceLists(instStatEntity);
		}
		
		// Inst Stat Phys (0x3F0000A)
		foreach (var instStatPhys in p3dFile.GetChunksOfType<InstStatPhysChunk>())
		{
			OffsetInstanceLists(instStatPhys);
		}
		
		// Anim Dyna Phys (0x3F0000E)
		foreach (var animDynaPhys in p3dFile.GetChunksOfType<AnimDynaPhysChunk>())
		{
			OffsetInstanceLists(animDynaPhys);
		}

		// Check if output file already exists.
		if (!forceOverwrite && File.Exists(outputPath))
		{
			Console.WriteLine($"Output file \"{outputPath}\" already exists.");
			Console.WriteLine("Do you want to overwrite? (Y/N): ");
			var response = Console.ReadLine();
			if (response != null && response.ToLower()[0] != 'y')
			{
				Console.WriteLine("Error: File could not be written.");
				Environment.Exit(4);
			}
		}

		// Write output file.
		p3dFile.Write(outputPath);
		Console.WriteLine("Successfully wrote file!");
		Environment.Exit(0);
	}

	// Get command line argument with error handling.
	static string GetArgValue(string[] args, int i)
	{
		if (i == args.Length)
		{
			Console.WriteLine($"Error: No value provided for {args[i]}.");
			Environment.Exit(1);
		}
		return args[i + 1];
	}

	static Vector3 MultMatrixByVector(Matrix4x4 matrix, Vector3 vector)
	{
		return new Vector3(matrix.M11 * vector.X + matrix.M21 * vector.Y + matrix.M31 * vector.Z,
			matrix.M12 * vector.X + matrix.M22 * vector.Y + matrix.M32 * vector.Z,
			matrix.M13 * vector.X + matrix.M23 * vector.Y + matrix.M33 * vector.Z);
	}

	// Offset Chunk Methods //
	// Mesh (0x10000)
	static void OffsetMeshes(Chunk chunk)
	{
		foreach (var mesh in chunk.GetChunksOfType<MeshChunk>())
		{
			foreach (var oldPrimitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
			{
				var positionList = oldPrimitiveGroup.GetFirstChunkOfType<PositionListChunk>();

				if (positionList != null)
				{
					for (int i = 0; i < positionList.Positions.Count; i++)
					{
						positionList.Positions[i] = Vector3.Transform(positionList.Positions[i], transform);
					}
				}
			}

			var boundingBox = mesh.GetFirstChunkOfType<BoundingBoxChunk>();

			if (boundingBox != null)
			{
				boundingBox.Low = Vector3.Transform(boundingBox.Low, transform);
				boundingBox.High = Vector3.Transform(boundingBox.High, transform);
			}

			var boundingSphere = mesh.GetFirstChunkOfType<BoundingSphereChunk>();

			if (boundingSphere != null)
			{
				boundingSphere.Centre = Vector3.Transform(boundingSphere.Centre, transform);
			}
		}
	}

	// Collision Object (0x7010000)
	static void OffsetCollisionObjects(Chunk chunk)
	{
		foreach (var collisionObject in chunk.GetChunksOfType<CollisionObjectChunk>())
		{
			OffsetCollisionVolumes(collisionObject);
		}
	}
	
	// Collision Volume (0x7010001)
	static void OffsetCollisionVolumes(Chunk chunk)
	{
		foreach (var collisionVolume in chunk.GetChunksOfType<CollisionVolumeChunk>())
		{
			OffsetCollisionVolumes(collisionVolume);

			foreach (var collisionBox in collisionVolume.GetChunksOfType<CollisionOrientedBoundingBoxChunk>())
			{
				var vectors = collisionBox.GetChunksOfType<CollisionVectorChunk>();
				if (vectors.Length != 4) continue;
				
				// Construct matrix from vectors.
				var boxRot = new Matrix4x4(vectors[1].Vector.X, vectors[1].Vector.Y, vectors[1].Vector.Z, 0,
					vectors[2].Vector.X, vectors[2].Vector.Y, vectors[2].Vector.Z, 0,
					vectors[3].Vector.X, vectors[3].Vector.Y, vectors[3].Vector.Z, 0,
					vectors[0].Vector.X, vectors[0].Vector.Y, vectors[0].Vector.Z, 1);
					
				// Apply transform to matrix.
				var newBoxRot = boxRot * transform;
					
				// Convert matrix back to vectors.
				vectors[0].Vector = new Vector3(newBoxRot.M41, newBoxRot.M42, newBoxRot.M43);
				vectors[1].Vector = new Vector3(newBoxRot.M11, newBoxRot.M12, newBoxRot.M13);
				vectors[2].Vector = new Vector3(newBoxRot.M21, newBoxRot.M22, newBoxRot.M23);
				vectors[3].Vector = new Vector3(newBoxRot.M31, newBoxRot.M32, newBoxRot.M33);
			}

			foreach (var collisionCylinder in collisionVolume.GetChunksOfType<CollisionCylinderChunk>())
			{
				var vectors = collisionCylinder.GetChunksOfType<CollisionVectorChunk>();
				if (vectors.Length != 2) continue;
				
				// Apply transform to centre vector.
				vectors[0].Vector = Vector3.Transform(vectors[0].Vector, transform);

				// Apply transform to direction vector.
					// Collision Cylinders don't store the entire rotation matrix, they store only the centre column as a vector.
					// If you multiply the transform matrix by the direction vector (rather than multiplying the vector by the
					// matrix, as you normally would), you get a new vector that equals the original rotated by the transform matrix.
					// I don't understand why this works, but it does so...hooray!
				vectors[1].Vector = MultMatrixByVector(transform, vectors[1].Vector);
			}

			foreach (var collisionSphere in collisionVolume.GetChunksOfType<CollisionSphereChunk>())
			{
				var vectorCentre = collisionSphere.GetFirstChunkOfType<CollisionVectorChunk>();
				if (vectorCentre == null) continue;
				
				vectorCentre.Vector = Vector3.Transform(vectorCentre.Vector, transform);
			}
		}
	}
	
	// Instance List (0x30000008)
	static void OffsetInstanceLists(Chunk chunk)
	{
		foreach (var instanceList in chunk.GetChunksOfType<InstanceListChunk>())
		{
			foreach (var scenegraph in instanceList.GetChunksOfType<ScenegraphChunk>())
			{
				foreach (var scenegraphRoot in scenegraph.GetChunksOfType<OldScenegraphRootChunk>())
				{
					foreach (var scenegraphBranch in scenegraphRoot.GetChunksOfType<OldScenegraphBranchChunk>())
					{
						foreach (var scenegraphTransform in scenegraphBranch.GetChunksOfType<OldScenegraphTransformChunk>())
						{
							foreach (var subScenegraphTransform in scenegraphTransform.GetChunksOfType<OldScenegraphTransformChunk>())
							{
								subScenegraphTransform.Transform *= transform;
							}
						}
					}
				}
			}
		}
	}
}