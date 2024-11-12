using System.Diagnostics;
using System.Text.RegularExpressions;
using Humanizer;

namespace FfmpegSamples.DrawLabel
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string folderPath = @"C:\Users\sleepy\Desktop\cortes"; // Change this to your folder path
            string outputFolderPath = Path.Combine(folderPath, "output");
            Directory.CreateDirectory(outputFolderPath);

            string[] videoFiles = Directory.GetFiles(folderPath, "*.mp4")
                .OrderBy(f => ExtractNumber(Path.GetFileNameWithoutExtension(f)))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string concatFileList = Path.Combine(outputFolderPath, "concat_list.txt");

            using (StreamWriter concatListWriter = new StreamWriter(concatFileList))
            {
                foreach (var videoFile in videoFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(videoFile);
                    string normalizedFileName = NormalizeFileName(fileName);
                    string outputVideoPath = Path.Combine(outputFolderPath, $"{normalizedFileName}_label.mp4");

                    // Command to add a filename label to the video using FFmpeg with NVIDIA hardware acceleration
                    string ffmpegArgs = $"-hwaccel cuda -i \"{videoFile}\" -vf drawtext=\"fontfile='/Windows/Fonts/arial.ttf': text='{normalizedFileName}': x=(w-text_w)/2: y=h-(text_h*1.3): fontsize=47: fontcolor=white: box=1: boxcolor=black@0.5\" -c:v h264_nvenc -preset slow -b:v 1M -c:a copy \"{outputVideoPath}\"";
                    RunFFmpeg(ffmpegArgs);

                    concatListWriter.WriteLine($"file '{outputVideoPath.Replace("'", "'\\''")}'");
                }
            }

            // Concatenate all labeled videos into final.mp4
            string finalOutputPath = Path.Combine(outputFolderPath, "final.mp4");
            string concatArgs = $"-f concat -safe 0 -i \"{concatFileList}\" -c copy \"{finalOutputPath}\"";
            RunFFmpeg(concatArgs);

            Console.WriteLine("Processing complete. Final video saved as " + finalOutputPath);

            // Audit the final video
            AuditFinalVideo(outputFolderPath, finalOutputPath);
        }

        static int ExtractNumber(string fileName)
        {
            var match = Regex.Match(fileName, @"\d+");
            return match.Success ? int.Parse(match.Value) : int.MaxValue;
        }

        static string NormalizeFileName(string fileName)
        {
            var match = Regex.Match(fileName, @"^(\d+\s*-\s*)(.*)$");
            if (match.Success)
            {
                string prefix = match.Groups[1].Value;
                string textPart = match.Groups[2].Value;
                string normalizedTextPart = textPart.Transform(To.TitleCase).Dehumanize();
                return prefix + normalizedTextPart;
            }
            return fileName.Transform(To.TitleCase).Dehumanize();
        }

        static void RunFFmpeg(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
        }

        static void AuditFinalVideo(string outputFolderPath, string finalVideoPath)
        {
            if (!File.Exists(finalVideoPath))
            {
                Console.WriteLine("Final video does not exist.");
                return;
            }

            string[] videoFiles = Directory.GetFiles(outputFolderPath, "*_label.mp4").OrderBy(f => f).ToArray();
            TimeSpan finalVideoLength = GetVideoDuration(finalVideoPath);
            TimeSpan totalLength = TimeSpan.Zero;

            foreach (var videoFile in videoFiles)
            {
                TimeSpan videoLength = GetVideoDuration(videoFile);
                totalLength += videoLength;

                Console.WriteLine($"{Path.GetFileName(videoFile)} length: {videoLength}");
            }

            Console.WriteLine($"Final video length: {finalVideoLength}");
            Console.WriteLine($"Total length of individual videos: {totalLength}");

            if (finalVideoLength >= totalLength && finalVideoLength <= totalLength.Add(TimeSpan.FromSeconds(5)))
            {
                Console.WriteLine("The final video length is correct compared to the sum of the individual videos.");
            }
            else
            {
                Console.WriteLine("Warning: The final video length does not match the sum of the individual videos.");
            }
        }

        static TimeSpan GetVideoDuration(string videoFilePath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (double.TryParse(output, out double seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
                else
                {
                    throw new InvalidOperationException($"Could not determine duration of video: {videoFilePath}");
                }
            }
        }
    }
}
