using System;
using System.IO;
using System.Net;
using System.Text;
using SkiaSharp;

namespace AmpImgnizer
{
	class MainClass
	{
		public static int Main(string[] args)
		{
			if ((args?.Length ?? 0) == 0)
			{
				Console.WriteLine("no arguments.");
				return -1;
			}

			var filePaths = args[0];

			var fileList = filePaths.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

			foreach (var filePath in fileList)
			{
				Console.WriteLine($"{filePath}");
				var tempFilePath = filePath + ".new";
				var needProcess = false;

				//var cacheFile = args[1];
				if (!File.Exists(filePath))
				{
					Console.WriteLine($"{filePath} not found.");
					return -2;
				}

				using (var inStream = File.OpenRead(filePath))
				using (var outStream = File.OpenWrite(tempFilePath))
				{
					using (var reader = new StreamReader(inStream, Encoding.UTF8))
					using (var writer = new StreamWriter(outStream, Encoding.UTF8))
					{
						while (reader.Peek() >= 0)
						{
							var query = "<img src=\"";
							var line = reader.ReadLine();
							var startIndex = line.IndexOf(query, StringComparison.Ordinal);
							if (startIndex >= 0)
							{
								startIndex += query.Length;
								var endIndex = line.IndexOf("\"", startIndex, StringComparison.Ordinal);
								var imageSrc = line.Substring(startIndex, endIndex - startIndex);
								Console.WriteLine($"img={imageSrc}");

								if (imageSrc.StartsWith("http", StringComparison.Ordinal))
								{
									try
									{
										var size = GetImageSize(imageSrc);
										Console.WriteLine($"wid={size.Width}, hei={size.Height}");

										line = line.Replace("<img src=\"", $"<amp-img width=\"{size.Width}\" height=\"{size.Height}\" layout=\"responsive\" src=\"");
										needProcess = true;
									}
									catch (Exception ex)
									{
										Console.WriteLine($"ERROR:" + ex.StackTrace);
									}
								}
							}
							writer.WriteLine(line);
						}
					}
				}

				if (needProcess)
				{
					File.Delete(filePath);
					File.Move(tempFilePath, filePath);
				}
				else if (File.Exists(tempFilePath))
				{
					File.Delete(tempFilePath);
				}
			}

			return 0;
		}

		static SKSizeI GetImageSize(string imageSrc)
		{
			var req = WebRequest.Create(imageSrc);
			req.Method = "GET";
			using (var res = req.GetResponse())
			{
				var length = (int)res.ContentLength;
				var reader = new BinaryReader(res.GetResponseStream());
				var data = new byte[length];
				reader.Read(data, 0, length);

				var bmpInfo = SKBitmap.DecodeBounds(data);
				return bmpInfo.Size;
			}
		}
	}
}
