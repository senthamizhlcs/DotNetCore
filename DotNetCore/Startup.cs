﻿using Core.EF;
using DotNetCore.Middleware;
using DotNetCore.Others.Models;
using DotNetCore.Others.SignalR;
using HttpClientFactorySample.GitHub;
using HttpClientFactorySample.Handlers;
using HttpClientFactorySample.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;
using Polly;
using System;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace DotNetCore
{
    public class Startup
    {
        private IConfiguration _config;

        public Startup(IConfiguration configuration)
        {
            _config = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });
            services.AddSignalR();

            AddEF(services);
            AddSession(services);
            services.AddSingleton<DIAppData>();
            services.AddTransient<DIByObjectRequest>();
            services.AddScoped<DIByHTTPRequest>();
            
            // cache in memory
            services.AddMemoryCache();
            // caching response for middlewares
            services.AddResponseCaching();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
           .AddJwtBearer(options =>
           {
               options.TokenValidationParameters = new TokenValidationParameters
               {
                   //ValidateIssuer = true,
                   //ValidateAudience = true,
                   //ValidateLifetime = true,
                   //ValidateIssuerSigningKey = true,

                   //ValidIssuer = "http://localhost:5000",
                   //ValidAudience = "http://localhost:5000",
                   
                   IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("KeyForSignInSecret@1234"))
               };
           });

            AddMVC(services);
            AddHTTPClient(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            

            UseException(app, env);
            //app.UseStaticFiles();//Short circute when found file
             
            //// caching response for middlewares
            //app.UseResponseCaching();

            //app.UseCookiePolicy();
            //app.UseSignalR(routes =>
            //{
            //    routes.MapHub<ChatHub>("/chatHub");
            //});

            //app.UseAuthentication();

            UseAuthentication(app);

            app.UseSession(); //Enable user session

            UseMVC(app);//Short circute when found route

            //OtherMiddlewares(app);

            //UseAnonimouseMethod(app, env);
        }

        private static void AddHTTPClient(IServiceCollection services)
        {
            // basic usage
            #region snippet1
            services.AddHttpClient();
            #endregion

            // named client
            #region snippet2
            services.AddHttpClient("github", c =>
            {
                c.BaseAddress = new Uri("https://api.github.com/");
                    // Github API versioning
                    c.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
                    // Github requires a user-agent
                    c.DefaultRequestHeaders.Add("User-Agent", "HttpClientFactory-Sample");
            });
            #endregion

            // typed client where configuration occurs in ctor
            #region snippet3
            services.AddHttpClient<GitHubService>();
            #endregion

            // typed client where configuration occurs during registration
            #region snippet4
            services.AddHttpClient<RepoService>(c =>
            {
                c.BaseAddress = new Uri("https://api.github.com/");
                c.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
                c.DefaultRequestHeaders.Add("User-Agent", "HttpClientFactory-Sample");
            });
            #endregion

            #region snippet5
            services.AddTransient<ValidateHeaderHandler>();

            services.AddHttpClient("externalservice", c =>
            {
                    // Assume this is an "external" service which requires an API KEY
                    c.BaseAddress = new Uri("https://localhost:5000/");
            })
            .AddHttpMessageHandler<ValidateHeaderHandler>();
            #endregion

            #region snippet6
            services.AddTransient<SecureRequestHandler>();
            services.AddTransient<RequestDataHandler>();

            services.AddHttpClient("clientwithhandlers")
                // This handler is on the outside and called first during the 
                // request, last during the response.
                .AddHttpMessageHandler<SecureRequestHandler>()
                // This handler is on the inside, closest to the request being 
                // sent.
                .AddHttpMessageHandler<RequestDataHandler>();
            #endregion

            #region snippet7
            services.AddHttpClient<UnreliableEndpointCallerService>()
                .AddTransientHttpErrorPolicy(p =>
                    p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(600)));
            #endregion

            #region snippet8
            var timeout = Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(10));
            var longTimeout = Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(30));

            services.AddHttpClient("conditionalpolicy")
                // Run some code to select a policy based on the request
                .AddPolicyHandler(request =>
                    request.Method == HttpMethod.Get ? timeout : longTimeout);
            #endregion

            #region snippet9
            services.AddHttpClient("multiplepolicies")
                .AddTransientHttpErrorPolicy(p => p.RetryAsync(3))
                .AddTransientHttpErrorPolicy(
                    p => p.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
            #endregion

            #region snippet10
            var registry = services.AddPolicyRegistry();

            registry.Add("regular", timeout);
            registry.Add("long", longTimeout);

            services.AddHttpClient("regulartimeouthandler")
                .AddPolicyHandlerFromRegistry("regular");
            #endregion

            #region snippet11
            services.AddHttpClient("extendedhandlerlifetime")
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));
            #endregion

            #region snippet12
            services.AddHttpClient("configured-inner-handler")
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new HttpClientHandler()
                    {
                        AllowAutoRedirect = false,
                        UseDefaultCredentials = true
                    };
                });
            #endregion
        }

        private void AddEF(IServiceCollection services)
        {
            //an instance from the DbContext pool is provided if available, rather than creating a new instance.
            services.AddDbContextPool<AppDbContext>(
            options => options.UseSqlServer(_config.GetConnectionString("EmployeeDBConnection")));

            //services.AddSingleton<IEmp, MockEmpRep>();
            //services.AddTransient<IEmployeeRepository, MockEmployeeRepository>();
            services.AddTransient<IEmployeeRepository, SQLEmployeeRepository>();
        }

        private static void OtherMiddlewares(IApplicationBuilder app)
        {
            //Other Middlewares.
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts(); //HTTP Strict Transport Security
        }

        private static void AddSession(IServiceCollection services)
        {
            services.AddSession(options =>
            {
                // Store the session ID in the request property bag.
                //request.Properties[SessionIdToken] = sessionId;

                //options.Cookie.Name = "SessionID";
                // Set a short timeout for easy testing.
                options.IdleTimeout = TimeSpan.FromSeconds(10);//10 Idle seconds time out 
                options.Cookie.HttpOnly = true;
                // Make the session cookie essential
                options.Cookie.IsEssential = true;
            });
        }

        private static void AddMVC(IServiceCollection services)
        {
            //services.AddMvcCore();//Basic future
            //Commented for JWT
            //services.AddMvc()
            //        .AddJsonOptions(options =>
            //        {
            //            options.SerializerSettings.Formatting = Formatting.Indented;
            //        });

            //Added for JWT
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }

        private static void UseAuthentication(IApplicationBuilder app)
        {
            //app.UseAuthentication();
            //app.UseMiddleware<AuthenticationMiddleware>();
            app.UseWhen(x => (x.Request.Path.StartsWithSegments("/api/Authentication", StringComparison.OrdinalIgnoreCase)),
            builder =>
            {
                builder.UseMiddleware<AuthenticationMiddleware>();
            });
            //app.Map("/api/Authentication", AuthenticationMiddleware); todo

        }

        private static void UseAnonimouseMethod(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.Use(async (context, next) =>
            {
                await context.Response.WriteAsync("Before Use;\n\n");
                await next.Invoke();
                await context.Response.WriteAsync("After Use;\n\n");
            });

            //app.Run is a terminal midleware
            app.Run(async (context) =>
            {

                if (env.IsDevelopment() & 1 == 2)
                {
                    await context.Response.WriteAsync("Hello World!");
                    //The internal web server is Kestrel.
                    //External web server can be IIS, Nginx or Apache.
                    //Kestrel is a cross-platform web server for ASP.NET Core.
                    //In Kestrel, the process used to host the app is dotnet.exe.
                    //Command line C:\Selvan\Sty\Core\Core>dotnet run

                    //CommandName       AspNetCoreHostingModel  Internal Web Server External Web Server
                    //Project           Hosting Setting Ignored Only one web server is used - Kestrel
                    //IISExpress        InProcess               Only one web server is used - IIS Express
                    //IISExpress        OutOfProcess            Kestrel             IIS Express
                    //IIS               InProcess               Only one web server is used - IIS
                    //IIS               OutOfProcess            Kestrel             IIS
                    string ProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                    await context.Response.WriteAsync("\r\n" + "ProcessName : " + ProcessName);
                }
                //await context.Response.WriteAsync(_configuration["MyKey"]);
                await context.Response.WriteAsync(Environment.GetEnvironmentVariable("MyKey"));

            });
        }

        private static void UseMVC(IApplicationBuilder app)
        {
            //app.UseMvcWithDefaultRoute();
            //Routing - https://docs.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-2.2
            //When you run project it will start on "launchUrl". "launchUrl" is set to "api/values" in the project template. Nothing to do with MVC route that you changed
            //app.UseMvc(routes =>
            //{
            //    //routes.MapRoute("default", "{controller=Home}/{action=Index}/{id?}");
            //    routes.MapRoute(
            //    name: "default",
            //    template: "api/{controller}/{action}",
            //    defaults: new { controller = "Exception", action = "ThrowException" });
            //});

           app.UseMvc();            
        }

        private static void UseException(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseMiddleware<ErrorHandlingMiddleware>();

            if (env.IsDevelopment())
            {
                //app.UseDatabaseErrorPage();
                //app.UseDeveloperExceptionPage(); //HTML Error Response. With Stack: Error Line, Query, Cookies and Header.
            }
            else
            {
                //app.UseExceptionHandler();
                //app.UseExceptionHandler("/Error");
            }

            //app.UseExceptionHandler(a => a.Run(async context =>
            //{
            //    var feature = context.Features.Get<IExceptionHandlerPathFeature>();
            //    var exception = feature.Error;

            //    var result1 = JsonConvert.SerializeObject(new { error = exception.Message, sss = "" });
            //    var result = JsonConvert.SerializeObject(new VMResponse()
            //    {
            //        Error = new List<VMError>() {new VMError()
            //            {
            //                DisplayMessage = "Request Faild",
            //                ErrorMessage = exception.Message,
            //                RequestURI = context.Request.Path
            //            }
            //        },
            //        Status = false
            //    });
            //    context.Response.ContentType = "application/json";
            //    await context.Response.WriteAsync(result);
            //}));
        }
    }
}
