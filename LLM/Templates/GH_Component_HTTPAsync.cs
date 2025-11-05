using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel;

namespace LLM.Templates
{
    /// <summary>
    /// Base class for asynchronous HTTP-based Grasshopper components.
    /// Subclasses can call POSTAsync to perform an HTTP POST and handle callbacks via SolveInstance.
    /// </summary>
    public abstract class GH_Component_HTTPAsync : GH_Component
    {
        protected enum RequestState { Off, Idle, Requesting, Done, Error }
        protected RequestState _currentState = RequestState.Idle;
        protected bool _shouldExpire = false;
        protected string _response = string.Empty;

        /// <summary>
        /// Constructs a new async HTTP component with the specified metadata.
        /// </summary>
        protected GH_Component_HTTPAsync(string name, string nickname, string description, string category, string subCategory)
            : base(name, nickname, description, category, subCategory)
        {
        }

        /// <summary>
        /// Sends an HTTP POST asynchronously to the given URL.
        /// </summary>
        /// <param name="url">The endpoint URL.</param>
        /// <param name="body">The request body (JSON or form data).</param>
        /// <param name="contentType">The MIME type of the request body.</param>
        /// <param name="authToken">Optional bearer token (empty for none).</param>
        /// <param name="timeout">Timeout in milliseconds.</param>
        protected void POSTAsync(string url, string body, string contentType, string authToken, int timeout)
        {
            try
            {
                var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeout) };
                if (!string.IsNullOrEmpty(authToken))
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                var content = new StringContent(body, Encoding.UTF8, contentType);
                Task.Run(async () =>
                {
                    try
                    {
                        var resp = await client.PostAsync(url, content).ConfigureAwait(false);
                        string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            _response = respBody;
                            _currentState = RequestState.Done;
                        }
                        else
                        {
                            _response = $"HTTP Error {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                            _currentState = RequestState.Error;
                        }
                    }
                    catch (Exception ex)
                    {
                        _response = ex.Message;
                        _currentState = RequestState.Error;
                    }
                    finally
                    {
                        _shouldExpire = true;
                        ExpireSolution(true);
                        client.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                _response = ex.Message;
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
            }
        }
    }
}