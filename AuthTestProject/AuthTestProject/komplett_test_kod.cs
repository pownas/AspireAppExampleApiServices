// ==================================================================================
// FIL: IntegrationTestsWithAuth.cs
// BESKRIVNING: Ett komplett, sammanhängande exempel på hur man sätter upp 
//              integrationstester i .NET 10 med MSTest och simulerad Bearer-token.
// ==================================================================================

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntegrationTestingDemo
{
    // ==============================================================================
    // 2. TEST-AUTENTISERINGSHANTERARE (TESTPROJEKTET)
    // ==============================================================================
    // Denna klass körs ENDAST under testning. Den lyssnar efter en specifik test-token
    // och skapar en giltig ClaimsPrincipal om token matchar.
    public class TestAuthHandlerOptions : AuthenticationSchemeOptions
    {
        // Kan byggas ut om du vill skicka konfigurationsparametrar till din test-auth
    }

    public class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
    {
        public const string AuthenticationScheme = "TestScheme";

        public TestAuthHandler(
            IOptionsMonitor<TestAuthHandlerOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Kontrollera om Authorization-headern finns i anropet
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var authHeader = Request.Headers["Authorization"].ToString();

            // Kontrollera att det rör sig om en Bearer-token
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();

            // Verifiera mot vår hårdkodade test-token. 
            // I mer avancerade scenarier kan du läsa claims direkt ur token om du vill.
            if (token != "MittHemligaTestToken")
            {
                return Task.FromResult(AuthenticateResult.Fail("Ogiltig test-token."));
            }

            // Om token är giltig, bygger vi upp den identitet som API:et ska se.
            // Här kan du konfigurera roller och behörigheter (scopes) fritt för testerna.
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "TestAnvändare"),
                new Claim(ClaimTypes.NameIdentifier, "999"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("scope", "read:data")
            };

            var identity = new ClaimsIdentity(claims, AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }


    // ==============================================================================
    // 3. ANPASSAD WEBAPPLICATIONFACTORY (TESTPROJEKTET)
    // ==============================================================================
    // Denna klass startar upp vår API-applikation i minnet och byter ut 
    // produktionsautentiseringen mot vår TestAuthHandler.
    public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                // Registrera vår TestAuthHandler och sätt den som standard
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                })
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                    TestAuthHandler.AuthenticationScheme, options => { });
            });
        }
    }


    // ==============================================================================
    // 4. INTEGRATIONSTESTER MED MSTEST (TESTPROJEKTET)
    // ==============================================================================
    // Här testar vi att autentiseringen fungerar som förväntat.
    [TestClass]
    public class SecureEndpointTests
    {
        private CustomWebApplicationFactory<Program> _factory = null!;
        private HttpClient _client = null!;

        [TestInitialize]
        public void Setup()
        {
            // Skapa testfabriken och källan till HTTP-anrop
            _factory = new CustomWebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Frigör resurser efter varje testkörning
            _client?.Dispose();
            _factory?.Dispose();
        }

        [TestMethod]
        public async Task GetSecureData_UtanToken_Returnerar401Unauthorized()
        {
            // Act: Gör ett anrop utan att bifoga någon token
            var response = await _client.GetAsync("/api/secure-data", CancellationToken.None);

            // Assert: API:et måste svara med 401 Unauthorized
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [TestMethod]
        public async Task GetSecureData_MedOgiltigToken_Returnerar401Unauthorized()
        {
            // Arrange: Skicka med en felaktig token
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "FelaktigToken");

            // Act: Utför anropet
            var response = await _client.GetAsync("/api/secure-data");

            // Assert: API:et nekar åtkomst (401) eftersom token är felaktig
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [TestMethod]
        public async Task GetSecureData_MedGiltigTestToken_Returnerar200OkOchData()
        {
            // Arrange: Bifoga vår giltiga test-token i Authorization-headern
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "MittHemligaTestToken");

            // Act: Utför anropet till den skyddade slutpunkten
            var response = await _client.GetAsync("/api/secure-data");

            // Assert: Kontrollera att anropet lyckades (200 OK) och innehåller rätt data
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var contentString = await response.Content.ReadAsStringAsync();
            using var jsonDocument = JsonDocument.Parse(contentString);
            var message = jsonDocument.RootElement.GetProperty("message").GetString();

            Assert.AreEqual("Detta är skyddad data!", message);
        }
    }
}





// Ytterligare testkod nedanför:



//// ==================================================================================
//// FIL: IntegrationTestsWithAuth.cs
//// BESKRIVNING: Ett komplett exempel på hur man sätter upp integrationstester 
////              i .NET 10 med MSTest och simulerad Bearer-token.
////              
//// ATT OBSERVERA: Denna fil innehåller nu ENBART testkod för att undvika 
////              kompileringsfelet CS0017 (dubbla entry points/Main-metoder).
//// ==================================================================================

//using System.Net;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Security.Claims;
//using System.Text.Encodings.Web;
//using System.Text.Json;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.Authentication;
//using Microsoft.AspNetCore.Hosting;
//using Microsoft.AspNetCore.Mvc.Testing;
//using Microsoft.AspNetCore.TestHost;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;
//using Microsoft.VisualStudio.TestTools.UnitTesting;

//// VIKTIGT: Byt ut "MyWebApiNamespace" till det namespace som ditt riktiga Web API använder.
//// Det är därifrån din riktiga "Program"-klass kommer att hämtas.
//// using MyWebApiNamespace; 

//namespace IntegrationTestingDemo
//{
//    // ==============================================================================
//    // 1. DUMMY-REPRESENTATION AV PROGRAM (Endast om du inte har refererat ditt API än)
//    // ==============================================================================
//    // Om du vill att denna fil ska kompilera direkt i ett isolerat testprojekt utan att
//    // referera till ett riktigt API, använder vi denna tomma klass. 
//    // Om du har lagt till en projektreferens till ditt riktiga Web API kan du ta bort 
//    // denna klass helt, då den riktiga Program-klassen kommer att användas istället.
//    public class Program DummyProgram
//    {
//        // Vi har tagit bort "static void Main" härifrån för att lösa CS0017!
//    }

//    // ==============================================================================
//    // 2. TEST-AUTENTISERINGSHANTERARE (TESTPROJEKTET)
//    // ==============================================================================
//    // Denna klass körs ENDAST under testning. Den lyssnar efter en specifik test-token
//    // och skapar en giltig ClaimsPrincipal om token matchar.
//    public class TestAuthHandlerOptions : AuthenticationSchemeOptions
//    {
//        // Kan byggas ut om du vill skicka konfigurationsparametrar till din test-auth
//    }

//    public class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
//    {
//        public const string AuthenticationScheme = "TestScheme";

//        public TestAuthHandler(
//            IOptionsMonitor<TestAuthHandlerOptions> options,
//            ILoggerFactory logger,
//            UrlEncoder encoder)
//            : base(options, logger, encoder)
//        {
//        }

//        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
//        {
//            // Kontrollera om Authorization-headern finns i anropet
//            if (!Request.Headers.ContainsKey("Authorization"))
//            {
//                return Task.FromResult(AuthenticateResult.NoResult());
//            }

//            var authHeader = Request.Headers["Authorization"].ToString();

//            // Kontrollera att det rör sig om en Bearer-token
//            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
//            {
//                return Task.FromResult(AuthenticateResult.NoResult());
//            }

//            var token = authHeader.Substring("Bearer ".Length).Trim();

//            // Verifiera mot vår hårdkodade test-token. 
//            if (token != "MittHemligaTestToken")
//            {
//                return Task.FromResult(AuthenticateResult.Fail("Ogiltig test-token."));
//            }

//            // Om token är giltig, bygger vi upp den identitet som API:et ska se.
//            // Här kan du konfigurera roller och behörigheter (scopes) fritt för testerna.
//            var claims = new[]
//            {
//                new Claim(ClaimTypes.Name, "TestAnvändare"),
//                new Claim(ClaimTypes.NameIdentifier, "999"),
//                new Claim(ClaimTypes.Role, "Admin"),
//                new Claim("scope", "read:data")
//            };

//            var identity = new ClaimsIdentity(claims, AuthenticationScheme);
//            var principal = new ClaimsPrincipal(identity);
//            var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

//            return Task.FromResult(AuthenticateResult.Success(ticket));
//        }
//    }


//    // ==============================================================================
//    // 3. ANPASSAD WEBAPPLICATIONFACTORY (TESTPROJEKTET)
//    // ==============================================================================
//    // Denna klass startar upp vår API-applikation i minnet och byter ut 
//    // produktionsautentiseringen mot vår TestAuthHandler.
//    //
//    // OBS: Om du refererar ditt riktiga API, ändra till <Program> istället för <DummyProgram>
//    // ==============================================================================
//    public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
//    {
//        protected override void ConfigureWebHost(IWebHostBuilder builder)
//        {
//            builder.ConfigureTestServices(services =>
//            {
//                // Registrera vår TestAuthHandler och sätt den som standard
//                services.AddAuthentication(options =>
//                {
//                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
//                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
//                })
//                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
//                    TestAuthHandler.AuthenticationScheme, options => { });
//            });
//        }
//    }


//    // ==============================================================================
//    // 4. INTEGRATIONSTESTER MED MSTEST (TESTPROJEKTET)
//    // ==============================================================================
//    // Här testar vi att autentiseringen fungerar som förväntat.
//    //
//    // OBS: Om du refererar ditt riktiga API, ändra till <Program> istället för <DummyProgram>
//    // ==============================================================================
//    [TestClass]
//    public class SecureEndpointTests
//    {
//        private CustomWebApplicationFactory<DummyProgram> _factory;
//        private HttpClient _client;

//        [TestInitialize]
//        public void Setup()
//        {
//            // Skapa testfabriken och källan till HTTP-anrop
//            _factory = new CustomWebApplicationFactory<DummyProgram>();
//            _client = _factory.CreateClient();
//        }

//        [TestCleanup]
//        public void Cleanup()
//        {
//            // Frigör resurser efter varje testkörning
//            _client?.Dispose();
//            _factory?.Dispose();
//        }

//        [TestMethod]
//        public async Task GetSecureData_UtanToken_Returnerar401Unauthorized()
//        {
//            // Act: Gör ett anrop utan att bifoga någon token
//            var response = await _client.GetAsync("/api/secure-data");

//            // Assert: API:et måste svara med 401 Unauthorized
//            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
//        }

//        [TestMethod]
//        public async Task GetSecureData_MedOgiltigToken_Returnerar401Unauthorized()
//        {
//            // Arrange: Skicka med en felaktig token
//            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "FelaktigToken");

//            // Act: Utför anropet
//            var response = await _client.GetAsync("/api/secure-data");

//            // Assert: API:et nekar åtkomst (401) eftersom token är felaktig
//            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
//        }

//        [TestMethod]
//        public async Task GetSecureData_MedGiltigTestToken_Returnerar200OkOchData()
//        {
//            // Arrange: Bifoga vår giltiga test-token i Authorization-headern
//            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "MittHemligaTestToken");

//            // Act: Utför anropet till den skyddade slutpunkten
//            var response = await _client.GetAsync("/api/secure-data");

//            // Assert: Kontrollera att anropet lyckades (200 OK) och innehåller rätt data
//            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

//            var contentString = await response.Content.ReadAsStringAsync();
//            using var jsonDocument = JsonDocument.Parse(contentString);
//            var message = jsonDocument.RootElement.GetProperty("message").GetString();

//            Assert.AreEqual("Detta är skyddad data!", message);
//        }
//    }
//}

