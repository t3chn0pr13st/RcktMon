using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APIServer.Models;
using CoreNgine.Models;
using CoreNgine.Shared;

namespace APIServer
{
    public class Startup
    {
        public Startup( IConfiguration configuration )
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices( IServiceCollection services )
        {
            services.AddCors( options =>
             {
                 options.AddPolicy( "CorsPolicy", policy =>
                 {

                     policy
                         .SetIsOriginAllowed(s => true)
                         .AllowCredentials()
                         .AllowAnyMethod()
                         .AllowAnyHeader();
                 } );
             } );
            services.AddSingleton<IMainModel, MainWebModel>();
            services.AddSingleton<StocksManager>();
            services.AddSignalR()
                .AddMessagePackProtocol();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure( IApplicationBuilder app, IWebHostEnvironment env )
        {
            if ( env.IsDevelopment() )
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection();

            app.UseRouting();
            app.UseCors( "CorsPolicy" );
            //app.UseAuthorization();

            app.UseEndpoints( endpoints =>
            {
                 endpoints.MapHub<StocksHub>("/stockshub");
                 endpoints.MapControllers();
            } );

            app.ApplicationServices.GetRequiredService<IMainModel>().Start();
        }
    }
}
