using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;
using System.Numerics;

const float deg2Rad = MathF.PI / 180;

// Initialise variables.
var inputPath = string.Empty;
var outputPath = string.Empty;
var forceOverwrite = false;
var offset = new Vector3(0, 0, 0);
var rotX = 0f;
var rotY = 0f;
var rotZ = 0f;

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
			offset.X = float.Parse(GetArgValue(args, i));
			break;
		case "-y":
			offset.Y = float.Parse(GetArgValue(args, i));
			break;
		case "-z":
			offset.Z = float.Parse(GetArgValue(args, i));
			break;
		case "-rx":
			rotX = float.Parse(GetArgValue(args, i));
			break;
		case "-ry":
			rotY = float.Parse(GetArgValue(args, i));
			break;
		case "-rz":
			rotZ = float.Parse(GetArgValue(args, i));
			break;
	}
}

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

var rotMtrx = Matrix4x4.CreateFromYawPitchRoll(rotY * deg2Rad, rotX * deg2Rad, rotZ * deg2Rad);
var transform = rotMtrx * Matrix4x4.CreateTranslation(offset);

// Create P3DFile Object
Console.WriteLine($"Reading {inputPath}...");
P3DFile p3dFile = new(inputPath);

foreach (var staticEntity in p3dFile.GetChunksOfType<StaticEntityChunk>())
{
	foreach (var mesh in staticEntity.GetChunksOfType<MeshChunk>())
	{
		foreach (var oldPrimitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
		{
			var positionList = oldPrimitiveGroup.GetFirstChunkOfType<PositionListChunk>();

			for (int i = 0; i < positionList.Positions.Count; i++)
			{
				var newPosition = Vector3.Transform(positionList.Positions[i], transform);
				positionList.Positions[i] = newPosition;
			}
		}
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
		Environment.Exit(4);
	}
}

// Write output file.
p3dFile.Write(outputPath);
Console.WriteLine("Sucessfully wrote file!");
Environment.Exit(0);
return;

// Methods //

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