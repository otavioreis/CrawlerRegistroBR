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
using System.Web;

namespace CrawlerRegistroBR
{
    class Program
    {
        private static HttpClient _httpClient;
        private static readonly string _urlWhoIsRegistroBR = "https://registro.br/2/whois";
        private static readonly string _currentAssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static readonly string _resultDirectory = $@"{_currentAssemblyPath}\DadosExtraidos";
        private static readonly string _resultProvisorioDirectory = $@"{_currentAssemblyPath}\DadosExtraidosProvisorio";
        private static readonly int _numberRetryIfReducedInformation = 200;
        private static readonly int _secondsNextRetry = 15;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Iniciando a execução");
            _httpClient = CreateHttpClient();

            List<Task> bagAddTasks = new List<Task>();

            var localArquivoGerado = $@"{_resultDirectory}\emails.txt";

            if (!Directory.Exists($@"{_resultDirectory}"))
                Directory.CreateDirectory($@"{_resultDirectory}");

            if (File.Exists(localArquivoGerado))
                File.Delete(localArquivoGerado);

            var extractedEmails = new HashSet<string>();

            var listaDominiosJaExtraidos = new HashSet<string>();
            var listaDominiosFaltantes = new HashSet<string>();

            string[] fileEntries = Directory.GetFiles(_resultProvisorioDirectory);

            Console.WriteLine("Determinando dominios que foram extraídos anteriormente");
            Console.WriteLine("-------------------------------------------------------");

            foreach (var file in fileEntries)
            {
                var dominio = file.Split('_')[1].Split(new string[] { ".txt" }, StringSplitOptions.None)[0].Replace(".dominio.inexistente", "");

                if (!string.IsNullOrEmpty(dominio))
                {
                    Console.WriteLine(dominio);
                    listaDominiosJaExtraidos.Add(dominio);

                    string[] emails = File.ReadAllLines(file);

                    foreach (var email in emails)
                    {
                        if (!string.IsNullOrEmpty(email))
                            extractedEmails.Add(email);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Determinando dominios que devem ser extraídos");
            Console.WriteLine("---------------------------------------------");

            string[] readText = File.ReadAllLines($@"{_currentAssemblyPath}\ListaURLs\listaUrls.txt");

            foreach (var dominio in readText)
            {
                if (!string.IsNullOrEmpty(dominio) && !listaDominiosJaExtraidos.Contains(dominio))
                {
                    Console.WriteLine(dominio);
                    listaDominiosFaltantes.Add(dominio);
                }
            }

            Console.WriteLine("Iniciando a execução para os dominios levantados");
            Console.WriteLine("------------------------------------------------");
            foreach (var dominio in listaDominiosFaltantes)
            {
                bagAddTasks.Add(Task.Run(async () =>
                {
                    Console.WriteLine(dominio);
                    var emails = await ExtractEmails(dominio);

                    if(emails.Count > 0 && emails[0] == "dominioInexistente")
                    {
                        //mesmo se não encontrar emails grava o arquivo só para não extrair novamente.
                        var localArquivoDominio = $@"{_resultProvisorioDirectory}\emails_{dominio}.dominio.inexistente.txt";

                        if (File.Exists(localArquivoDominio))
                            File.Delete(localArquivoDominio);

                        File.WriteAllLines(localArquivoDominio, emails);

                        Console.WriteLine($"Concluido para dominio: {dominio}");
                    }
                    else
                    {
                        if (emails.Count > 0)
                        {
                            extractedEmails.UnionWith(emails);
                        }

                        //mesmo se não encontrar emails grava o arquivo só para não extrair novamente.
                        var localArquivoDominio = $@"{_resultProvisorioDirectory}\emails_{dominio}.txt";

                        if (File.Exists(localArquivoDominio))
                            File.Delete(localArquivoDominio);

                        File.WriteAllLines(localArquivoDominio, emails);

                        Console.WriteLine($"Concluido para dominio: {dominio}");
                    }

                    
                }));
            }

            // aguarda todas as tasks terminar
            await Task.WhenAll(bagAddTasks.ToArray());

            var listaEmailsTratada = extractedEmails.Distinct().Where(r => r != null && !string.IsNullOrEmpty(r)).OrderBy(r => r);

            File.WriteAllLines(localArquivoGerado, listaEmailsTratada);

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
            ServicePointManager.DefaultConnectionLimit = 5;
            ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;

            return httpClient;
        }

        private static async Task<List<string>> ExtractEmails(string dominio)
        {
            var extractedEmails = new List<string>();

            bool tryAgain = true;
            int tryCount = 0;
            while (tryAgain)
            {
                try
                {
                    var values = new FormUrlValueCollection();
                    values.Add("qr", dominio);
                    values.Add("captcha-selected", "0");
                    values.Add("g-recaptcha-response", "");

                    var content = new FormUrlEncodedContent(values);

                    var result = await _httpClient.PostAsync(_urlWhoIsRegistroBR, content);

                    if (result.IsSuccessStatusCode)
                    {
                        var html = await result.Content.ReadAsStringAsync();

                        if(!html.Contains(string.Format("Recurso {0} inexistente", dominio)) &&
                           !html.Contains(string.Format("O domínio {0} não pode ser registrado por estar aguardando o início do processo de liberação", dominio)))
                        {
                            if (!html.Contains("Taxa máxima de consultas excedida. Informações reduzidas") &&
                                !html.Contains("Problema interno") &&
                                !html.Contains("Erro no servidor, tente novamente mais tarde"))
                            {
                                var regex = new Regex(@"e-mail:        (.*?)\n", RegexOptions.IgnoreCase);
                                var matches = regex.Matches(html);
                                foreach (Match matchEmail in matches)
                                {
                                    if (matchEmail.Success)
                                    {
                                        var email = matchEmail.Groups[1].Value;
                                        email = email.Trim();
                                        if (!string.IsNullOrEmpty(email))
                                            extractedEmails.Add(HttpUtility.HtmlDecode(email));
                                    }
                                }

                                tryAgain = false;
                            }
                            else
                            {
                                if (tryCount == _numberRetryIfReducedInformation)
                                {
                                    Console.WriteLine($"Limite de tentativas atingido para a url {dominio}. Execução abortada.");
                                    tryAgain = false;
                                }
                                else
                                {
                                    Console.WriteLine($"Tentativa {tryCount + 1} de {_numberRetryIfReducedInformation} - Informações reduzidas. Tentando novamente em {_secondsNextRetry} segundos para url: {dominio}");
                                    await Task.Delay(_secondsNextRetry * 1000);
                                }
                            }
                        }
                        else
                        {
                            extractedEmails.Add("dominioInexistente");
                            tryAgain = false;
                        }
                    }
                }
                catch (Exception)
                { }

                tryCount++;
            }

            return extractedEmails;
        }
    }
}
