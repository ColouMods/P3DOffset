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
	static Matrix4x4 transform;
	static Quaternion rotQuat;

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
		var rotMtrx = GetRotationMatrix(rotation.X * deg2Rad, rotation.Y * deg2Rad, rotation.Z * deg2Rad);
		
		// Convert rotation matrix to quaternion.
		rotQuat = Quaternion.CreateFromRotationMatrix(rotMtrx);
		
		// Convert input translation to translation matrix.
		var transMtrx = Matrix4x4.CreateTranslation(translation);
		
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

		// Offset Chunks
		foreach (var chunk in p3dFile.Chunks)
		{
			var type = chunk.GetType();

			switch (type)
			{
				// Static Entity (0x3F00000)
				case var t when t == typeof(StaticEntityChunk):
					foreach (var mesh in chunk.GetChunksOfType<MeshChunk>())
					{
						float? lowX = null, lowY = null, lowZ = null;
						float? highX = null, highY = null, highZ = null;
						
						foreach (var primitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
						{
							var positionList = primitiveGroup.GetFirstChunkOfType<PositionListChunk>();
							if (positionList == null) continue;
							
							for (int i = 0; i < positionList.Positions.Count; i++)
							{
								// Apply transform to each vertex position.
								var pos = positionList.Positions[i];
								var newPos = Vector3.Transform(pos, transform);
								positionList.Positions[i] = newPos;

								// Find min and max X, Y and Z values.
								lowX = Math.Min(lowX ?? newPos.X, newPos.X); // If lowX is null, newPos.X is substituted in instead.
								lowY = Math.Min(lowY ?? newPos.Y, newPos.Y);
								lowZ = Math.Min(lowZ ?? newPos.Z, newPos.Z);

								highX = Math.Max(highX ?? newPos.X, newPos.X);
								highY = Math.Max(highY ?? newPos.Y, newPos.Y);
								highZ = Math.Max(highZ ?? newPos.Z, newPos.Z);
							}
						}
						
						// Set Bounding Box values.
						var bbLow = new Vector3(lowX ?? 0, lowY ?? 0, lowZ ?? 0);
						var bbHigh = new Vector3(highX ?? 0, highY ?? 0, highZ ?? 0);

						var boundingBox = mesh.GetFirstChunkOfType<BoundingBoxChunk>();

						if (boundingBox != null)
						{
							boundingBox.Low = bbLow;
							boundingBox.High = bbHigh;
						}

						// Set Bounding Sphere values.
						var boundingSphere = mesh.GetFirstChunkOfType<BoundingSphereChunk>();

						if (boundingSphere != null)
						{
							boundingSphere.Centre = new Vector3(
								(bbLow.X + bbHigh.X) / 2,
								(bbLow.Y + bbHigh.Y) / 2,
								(bbLow.Z + bbHigh.Z) / 2);
							
							// Find furthest away vertex from the bounding sphere centre.
							float? maxDist = null;
							foreach (var primitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
							{
								var positionList = primitiveGroup.GetFirstChunkOfType<PositionListChunk>();
								if (positionList == null) continue;

								foreach (var position in positionList.Positions)
								{
									var dist = Vector3.Distance(position, boundingSphere.Centre);
									maxDist = Math.Max(maxDist ?? dist, dist);
								}
							}
							
							boundingSphere.Radius = maxDist ?? 0;
						}
					}
					break;

				// Static Phys (0x3F00001)
				case var t when t == typeof(StaticPhysChunk):
					foreach (var collisionObject in chunk.GetChunksOfType<CollisionObjectChunk>())
					{
						OffsetCollisionVolumes(collisionObject);
					}
					break;

				// Dyna Phys (0x3F00002), Inst Stat Entity (0x3F00008), Inst Stat Phys (0x3F0000A), & Anim Dyna Phys (0x3F0000E)
				case var t when t == typeof(DynaPhysChunk) || t == typeof(InstStatEntityChunk) || t == typeof(InstStatPhysChunk) || t == typeof(AnimDynaPhysChunk):
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
					break;
			}
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
	
	static Matrix4x4 GetRotationMatrix(float x, float y, float z)
	{
		var xMtrx = new Matrix4x4(
			1f, 0f, 0f, 0f,
			0f, MathF.Cos(x), MathF.Sin(x), 0f,
			0f, -MathF.Sin(x), MathF.Cos(x), 0f,
			0f, 0f, 0f, 1f
		);
    
		var yMtrx = new Matrix4x4(
			MathF.Cos(y), 0f, -MathF.Sin(y), 0f,
			0f, 1f, 0f, 0f,
			MathF.Sin(y), 0f, MathF.Cos(y), 0f,
			0f, 0f, 0f, 1f
		);
    
		var zMtrx = new Matrix4x4(
			MathF.Cos(z), MathF.Sin(z), 0f, 0f,
			-MathF.Sin(z), MathF.Cos(z), 0f, 0f,
			0f, 0f, 1f, 0f,
			0f, 0f, 0f, 1f
		);
		
		return zMtrx * yMtrx * xMtrx;
	}

	// Offset Chunk Methods //
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
				var rot = vectors[1].Vector;
				// Collision Cylinders don't store the entire rotation matrix, they store only the centre column as a vector.
				// As such, applying the transform isn't as simple as multiplying matrices.
				// But, by complete accident, I stumbled upon a formula that seems to work? (though I don't understand why)
				vectors[1].Vector = new Vector3(
					transform.M11 * rot.X + transform.M21 * rot.Y + transform.M31 * rot.Z,
					transform.M12 * rot.X + transform.M22 * rot.Y + transform.M32 * rot.Z,
					transform.M13 * rot.X + transform.M23 * rot.Y + transform.M33 * rot.Z
				);
			}

			foreach (var collisionSphere in collisionVolume.GetChunksOfType<CollisionSphereChunk>())
			{
				var vectorCentre = collisionSphere.GetFirstChunkOfType<CollisionVectorChunk>();
				if (vectorCentre == null) continue;
				
				vectorCentre.Vector = Vector3.Transform(vectorCentre.Vector, transform);
			}
		}
	}
}