using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using SixLabors.ImageSharp;

namespace AmpImgnizer
{
    class MainClass
    {
        public static int Main(string[] args)
        {
            if ((args?.Length ?? 0) != 1)
            {
                Console.WriteLine("invalid arguments.");
                return -1;
            }

            var filePaths = args[0];
            var siteRootDir = Directory.GetCurrentDirectory();

            var fileList = filePaths.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            var imageSizeCachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "size_cache.json");
			IDictionary<string, ImgSize> cache = new Dictionary<string, ImgSize>();
            if (File.Exists(imageSizeCachePath))
            {
				try
				{
					using (var strem = File.OpenRead(imageSizeCachePath))
                    {
                        var reader = new StreamReader(strem);
						cache = JsonConvert.DeserializeObject<IDictionary<string, ImgSize>>(reader.ReadToEnd());
                    }
				}
				finally
				{

				}
            }

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
                            var line = reader.ReadLine();

                            // Replace <img>
                            {
                                var query = "<img src=\"";
                                var startIndex = line.IndexOf(query, StringComparison.Ordinal);
                                if (startIndex >= 0)
                                {
                                    startIndex += query.Length;
                                    var endIndex = line.IndexOf("\"", startIndex, StringComparison.Ordinal);
                                    var imageSrc = line.Substring(startIndex, endIndex - startIndex);
                                    Console.WriteLine($"img={imageSrc}");

                                    try
                                    {
										ImgSize size;
                                        if (cache.ContainsKey(imageSrc))
                                        {
                                            size = cache[imageSrc];
                                        }
                                        else
                                        {
                                            size = GetImageSize(siteRootDir, Path.GetDirectoryName(filePath), imageSrc);
                                            cache.Add(imageSrc, size);
                                        }

                                        Console.WriteLine($"wid={size.Width}, hei={size.Height}");

                                        line = line.Replace("<img src=\"", $"<amp-img width=\"{size.Width}\" height=\"{size.Height}\" layout=\"responsive\" src=\"");

                                        needProcess = true;
                                    }
                                    catch (Exception ex)
                                    {
										Console.WriteLine($"IMAGE REPLACE ERROR({ex.Message}):" + ex.StackTrace);
                                    }
                                }
                            }

                            // Replace twitter script
                            {
                                var query = "<blockquote class=\"twitter-tweet\"";
                                var startIndex = line.IndexOf(query, StringComparison.Ordinal);
                                if (startIndex >= 0)
                                {
                                    Console.WriteLine($"twitter script = {line}");
                                    // <blockquote class="twitter-tweet" data-lang="ja"><p lang="ja" dir="ltr">これだよこれがインスタントプログラミングだよ!</p>
                                    // &mdash; Atsushi Eno (@atsushieno) <a href="https://twitter.com/atsushieno/status/715566438203809792">2016年3月31日</a></blockquote>

                                    try
                                    {
                                        var tweetIdQuery = "/status/";
                                        var tweetIdStartIndex = line.IndexOf(tweetIdQuery, StringComparison.Ordinal) + tweetIdQuery.Length;
                                        var tweetIdEndIndex = line.IndexOf("\"", tweetIdStartIndex, StringComparison.Ordinal);
                                        var tweetId = line.Substring(tweetIdStartIndex, tweetIdEndIndex - tweetIdStartIndex);

                                        var messageQuery = "dir=\"ltr\">";
                                        var messageStartIndex = line.IndexOf(messageQuery, StringComparison.Ordinal) + messageQuery.Length;
                                        var messageEndIndex = line.IndexOf("</p>", messageStartIndex, StringComparison.Ordinal);
                                        var message = line.Substring(messageStartIndex, messageEndIndex - messageStartIndex);

                                        var senderQuery = "(@";
                                        var senderStartIndex = line.IndexOf(senderQuery, StringComparison.Ordinal) + senderQuery.Length;
                                        var senderEndIndex = line.IndexOf(")", senderStartIndex, StringComparison.Ordinal);
                                        var sender = line.Substring(senderStartIndex, senderEndIndex - senderStartIndex);

                                        line = $@"<amp-twitter data-tweetid=""{tweetId}"" width=""800"" height=""600"" layout=""responsive"" ></amp-twitter><!-- {message} by @{sender} -->";
                                        reader.ReadLine(); // 次の行(<script) を読み飛ばす

                                        needProcess = true;
                                    }
                                    catch (Exception ex)
                                    {
										Console.WriteLine($"TWEET REPLACE ERROR({ex.Message}):" + ex.StackTrace);
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

            using (var strem = File.OpenWrite(imageSizeCachePath))
            {
                var writer = new StreamWriter(strem);
                writer.Write(JsonConvert.SerializeObject(cache));
                writer.Flush();
            }

            return 0;
        }

		static ImgSize GetImageSize(string siteRootDir, string dir, string imageSrc)
        {
            var imageUri = new Uri(imageSrc);

            if (imageUri.Scheme.StartsWith("http", StringComparison.Ordinal))
            {
                var req = WebRequest.Create(imageSrc);
                req.Method = "GET";
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    if (res.StatusCode != HttpStatusCode.OK)
                    {
                        throw new FileNotFoundException($"Http status code = {res.StatusCode}");
                    }
                    var length = (int)res.ContentLength;

                    if (imageSrc.EndsWith("svg", StringComparison.Ordinal))
                    {
                        var svgDoc = Svg.SvgDocument.Open<Svg.SvgDocument>(res.GetResponseStream());
                        var dim = svgDoc.GetDimensions();
						return new ImgSize((int)dim.Width, (int)dim.Height);
                    }
                    else
                    {
                        //var reader = new BinaryReader(res.GetResponseStream());
                        //var data = new byte[length];
                        //reader.Read(data, 0, length);

                        //var bmpInfo = SKBitmap.DecodeBounds(data);
                        //return bmpInfo.Size;
						using (var image = Image.Load(res.GetResponseStream()))
                        {
							return new ImgSize(image.Width, image.Height);
                        }
                    }
                }
            }
            else
            {
                var absolutePath = imageUri.LocalPath;
                if (imageUri.IsAbsoluteUri)
                {
                    absolutePath = Path.Combine(siteRootDir, absolutePath.Substring(1));
                }
                else
                {
                    absolutePath = new Uri(new Uri(dir), imageUri).AbsolutePath;
                }

                if (!File.Exists(absolutePath))
                {
                    throw new FileNotFoundException($"{absolutePath} is not found.");
                }

                if (absolutePath.EndsWith("svg", StringComparison.Ordinal))
                {
                    var svgDoc = Svg.SvgDocument.Open(absolutePath);
                    var dim = svgDoc.GetDimensions();
					return new ImgSize((int)dim.Width, (int)dim.Height);
                }
                else
                {
                    //var bmpInfo = SKBitmap.DecodeBounds(absolutePath);
                    //return bmpInfo.Size;
					using (var image = Image.Load(absolutePath))
                    {
                        return new ImgSize(image.Width, image.Height);
                    }
                }
            }
        }
    }

	public class ImgSize
	{
		public ImgSize() {
			
		}

		public ImgSize(int width, int height)
		{
			Width = width;
			Height = height;
		}

		public int Width { get; set; }
		public int Height { get; set; }
	}
}
