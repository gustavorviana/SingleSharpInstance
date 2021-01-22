using System;

namespace SingleSharpInstance
{
    public struct SingleName
    {
        public SingleName(Guid id, string userName)
        {
            if (userName != null && (userName.Contains("\\") || userName.Contains("/")))
                throw new Exception("Invalid user name");

            this.UserName = userName;
            this.Id = id;
        }

        public string UserName { get; }

        public Guid Id { get; }

        public string GetMutexName()
        {
            var user = this.UserName?.ToUpper();
            var id = this.Id.ToString().Replace("-", "");

            return $"{user}/{id}";
        }

        public string GetPipeName()
        {
            return this.GetMutexName().Replace('/', '\\');
        }

        public override string ToString()
        {
            return this.GetMutexName();
        }
    }
}
