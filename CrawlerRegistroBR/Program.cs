using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CrawlerRegistroBR
{
    class Program
    {
        private static HttpClient _httpClient;
        private static readonly string _urlWhoIsRegistroBR = "https://registro.br/2/whois";
        private static readonly string _currentAssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static readonly string _generatedImagesDirectory = $@"{_currentAssemblyPath}\DadosExtraidos";
        private static readonly int _numberRetryIfReducedInformation = 100;
        private static readonly int _secondsNextRetry = 30;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Iniciando a execução");
            _httpClient = CreateHttpClient();

            string[] readText = File.ReadAllLines($@"{_currentAssemblyPath}\ListaURLs\listaUrls.txt");


            ConcurrentBag<int> cb = new ConcurrentBag<int>();
            List<Task> bagAddTasks = new List<Task>();

            var localArquivoGerado = $@"{_generatedImagesDirectory}\emails.txt";

            if (!Directory.Exists($@"{_generatedImagesDirectory}"))
                Directory.CreateDirectory($@"{_generatedImagesDirectory}");

            if (File.Exists(localArquivoGerado))
                File.Delete(localArquivoGerado);

            var extractedEmails = new List<string>();

            foreach (var url in readText)
            {
                bagAddTasks.Add(Task.Run(async () =>
                {
                    Console.WriteLine($"{url}");
                    var emails = await ExtractEmails(url);
                    if(emails.Count > 0)
                        extractedEmails.AddRange(emails);

                    Console.WriteLine($"Concluido para url: {url}");
                }));
            }

            // aguarda todas as tasks terminar
            await Task.WhenAll(bagAddTasks.ToArray());

            File.WriteAllLines(localArquivoGerado, extractedEmails);

            Console.WriteLine("-------------------");
            Console.WriteLine("Todas as extrações foram concluídas" + Environment.NewLine);

            Console.WriteLine("Fim da execução");

            Console.ReadLine();
        }

        private static HttpClient CreateHttpClient()
        {
            var httpClientHandler = new WebRequestHandler();
            httpClientHandler.UseCookies = false;

            //debug: fiddler
            //httpClientHandler.Proxy = new WebProxy("127.0.0.1:8888");

            var httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.ExpectContinue = false;

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.92 Safari/537.36");

            //default limit = 2
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;

            return httpClient;
        }

        private static async Task<List<string>> ExtractEmails(string url)
        {
            var extractedEmails = new List<string>();

            bool tryAgain = true;
            int tryCount = 0;
            while (tryAgain)
            {
                var values = new FormUrlValueCollection();
                values.Add("qr", url);
                values.Add("captcha-selected", "0");
                values.Add("g-recaptcha-response", "");

                var content = new FormUrlEncodedContent(values);

                var result = await _httpClient.PostAsync(_urlWhoIsRegistroBR, content);

                if (result.IsSuccessStatusCode)
                {
                    var html = await result.Content.ReadAsStringAsync();

                    if(!html.Contains("Taxa máxima de consultas excedida. Informações reduzidas"))
                    {
                        tryAgain = false;

                        var regex = new Regex(@"e-mail:        (.*?)\n", RegexOptions.IgnoreCase);
                        var matches = regex.Matches(html);
                        foreach (Match matchEmail in matches)
                        {
                            if (matchEmail.Success)
                            {
                                var email = matchEmail.Groups[1].Value;
                                email = email.Trim();
                                if (!string.IsNullOrEmpty(email))
                                    extractedEmails.Add(email);
                            }
                        }
                    }
                    else
                    {
                        if (tryCount == _numberRetryIfReducedInformation)
                        {
                            Console.WriteLine($"Limite de tentativas atingido para a url {url}. Execução abortada.");
                            tryAgain = false;
                        }
                        else
                        {
                            Console.WriteLine($"Tentativa {tryCount + 1} de {_numberRetryIfReducedInformation} - Informações reduzidas. Tentando novamente em {_secondsNextRetry} segundos para url: {url}");
                            await Task.Delay(_secondsNextRetry * 1000);
                        }
                    }
                }

                tryCount++;
            }

            return extractedEmails;
        }
    }
}
