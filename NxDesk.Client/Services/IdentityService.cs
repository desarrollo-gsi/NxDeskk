using System.IO;

namespace NxDesk.Client.Services
{
    public class IdentityService
    {
        public string MyID { get; private set; }
        public string MyAlias { get; private set; }
        private readonly string _configPath;
        private readonly string _idFilePath;

        public IdentityService()
        {
            _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NxDesk");
            _idFilePath = Path.Combine(_configPath, "client.id");
            LoadOrCreateIdentity();
            MyAlias = Environment.MachineName;
        }

        private void LoadOrCreateIdentity()
        {
            if (!Directory.Exists(_configPath))
            {
                Directory.CreateDirectory(_configPath);
            }

            if (File.Exists(_idFilePath))
            {
                MyID = File.ReadAllText(_idFilePath);

                if (string.IsNullOrWhiteSpace(MyID) || MyID.Length != 9)
                {
                    MyID = CreateNewID();
                    File.WriteAllText(_idFilePath, MyID);
                }
            }
            else
            {
                MyID = CreateNewID();
                File.WriteAllText(_idFilePath, MyID);
            }
        }

        private string CreateNewID()
        {
            Random rand = new Random();
            return rand.Next(100_000_000, 999_999_999).ToString();
        }
    }
}