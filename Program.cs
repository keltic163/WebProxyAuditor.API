
using System.Reflection;
using WebProxyAuditor.API.Services;

namespace WebProxyAuditor.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlFilePath = Path.Combine(AppContext.BaseDirectory, xmlFileName);

                if (File.Exists(xmlFilePath))
                {
                    options.IncludeXmlComments(xmlFilePath, includeControllerXmlComments: true);
                }
            });
            builder.Services.AddScoped<IWebScraperService, WebScraperService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
